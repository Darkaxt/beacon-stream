[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$EvidenceDirectory,
    [switch]$ArtifactsReady,
    [switch]$SessionOnly,
    [switch]$ValidateKestrelParser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'gate3-validation-common.ps1')

# The job owns only the Server process and inherited Worker children.
if ($null -eq ('Beacon.Gate3.OwnedProcessJob' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace Beacon.Gate3
{
    public sealed class OwnedProcessJob : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private IntPtr handle;

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job, int informationClass, IntPtr information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr value);

        public OwnedProcessJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var information = new ExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            var length = Marshal.SizeOf(typeof(ExtendedLimitInformation));
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                if (!SetInformationJobObject(handle, 9, buffer, (uint)length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
                throw;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var value = handle;
            handle = IntPtr.Zero;
            CloseHandle(value);
            GC.SuppressFinalize(this);
        }
    }

    public sealed class ProcessOutputCapture : IDisposable
    {
        private readonly ConcurrentQueue<string> standardOutput = new();
        private readonly ConcurrentQueue<string> standardError = new();
        private readonly ManualResetEvent listening = new(false);
        private readonly ManualResetEvent exited = new(false);
        private readonly ManualResetEvent errorObserved = new(false);
        private readonly object callbackGate = new();
        private Process process;
        private string listeningAddress;
        private int activeCallbacks;
        private bool finalizing;

        public string StandardOutput => string.Join(Environment.NewLine, standardOutput);
        public string StandardError => string.Join(Environment.NewLine, standardError);

        public void Attach(Process value)
        {
            if (value == null || process != null)
            {
                throw new InvalidOperationException("Process output capture can be attached once.");
            }
            process = value;
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnError;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process.HasExited)
            {
                exited.Set();
            }
        }

        public string WaitForListeningAddress()
        {
            var winner = WaitHandle.WaitAny(new[] { listening, exited });
            if (winner != 0 || string.IsNullOrWhiteSpace(listeningAddress))
            {
                throw new InvalidOperationException(
                    "Beacon.Server exited before a structured Kestrel listening event.");
            }
            return listeningAddress;
        }

        public void WaitForStandardError() => errorObserved.WaitOne();

        public void WaitForDrain()
        {
            if (process == null || !process.HasExited)
            {
                throw new InvalidOperationException(
                    "Process output can be finalized only after owner exit.");
            }
            lock (callbackGate)
            {
                finalizing = true;
            }
            try
            {
                process.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
            }
            try
            {
                process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
            lock (callbackGate)
            {
                while (activeCallbacks != 0)
                {
                    Monitor.Wait(callbackGate);
                }
            }
        }

        public void Dispose()
        {
            if (process != null)
            {
                process.Exited -= OnExited;
                process.OutputDataReceived -= OnOutput;
                process.ErrorDataReceived -= OnError;
                process = null;
            }
            listening.Dispose();
            exited.Dispose();
            errorObserved.Dispose();
        }

        private void OnExited(object sender, EventArgs eventArgs)
        {
            if (!TryEnterCallback()) return;
            try
            {
                exited.Set();
            }
            finally
            {
                ExitCallback();
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            if (!TryEnterCallback()) return;
            try
            {
                if (eventArgs.Data == null) return;
                standardOutput.Enqueue(eventArgs.Data);
                if (TryGetListeningAddress(eventArgs.Data, out var address))
                {
                    listeningAddress = address;
                    listening.Set();
                }
            }
            finally
            {
                ExitCallback();
            }
        }

        private void OnError(object sender, DataReceivedEventArgs eventArgs)
        {
            if (!TryEnterCallback()) return;
            try
            {
                if (eventArgs.Data == null) return;
                standardError.Enqueue(eventArgs.Data);
                errorObserved.Set();
            }
            finally
            {
                ExitCallback();
            }
        }

        private bool TryEnterCallback()
        {
            lock (callbackGate)
            {
                if (finalizing) return false;
                activeCallbacks++;
                return true;
            }
        }

        private void ExitCallback()
        {
            lock (callbackGate)
            {
                activeCallbacks--;
                if (activeCallbacks == 0)
                {
                    Monitor.PulseAll(callbackGate);
                }
            }
        }

        private static bool TryGetListeningAddress(string line, out string address)
        {
            address = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("EventId", out var eventId) ||
                    eventId.GetInt32() != 14 ||
                    !root.TryGetProperty("State", out var state) ||
                    !state.TryGetProperty("address", out var value))
                {
                    return false;
                }
                address = value.GetString();
                return !string.IsNullOrWhiteSpace(address);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
'@
}

function Require-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Write-Gate3Stage([string]$Stage) {
    Write-Output "BEACON_GATE3_STAGE $Stage"
}

function Get-KestrelListeningAddress([string]$Line) {
    try {
        $record = $Line | ConvertFrom-Json
        if ([int]$record.EventId -ne 14 -or $null -eq $record.State) {
            return $null
        }
        $address = $record.State.PSObject.Properties['address']
        if ($null -eq $address -or [string]::IsNullOrWhiteSpace([string]$address.Value)) {
            return $null
        }
        return [string]$address.Value
    } catch {
        return $null
    }
}

function Get-HostServerUrl([Uri]$ListeningUri) {
    return "$($ListeningUri.Scheme)://127.0.0.1:$($ListeningUri.Port)"
}

function Assert-KestrelListeningFixture() {
    $kestrelFixture = '{"EventId":14,"State":{"address":"http://0.0.0.0:43125"}}'
    Require-Condition (
        (Get-KestrelListeningAddress $kestrelFixture) -eq 'http://0.0.0.0:43125') `
        'Structured Kestrel listening fixture was not parsed.'
    Require-Condition (
        (Get-HostServerUrl ([Uri]'http://0.0.0.0:43125')) -eq 'http://127.0.0.1:43125') `
        'Host loopback URL fixture was not mapped.'
    Require-Condition (
        (Get-HostServerUrl ([Uri]'https://0.0.0.0:43126')) -eq 'https://127.0.0.1:43126') `
        'Secure host loopback URL fixture was not mapped.'
}

function Assert-AndroidInstrumentationFixture() {
    $success = @'
INSTRUMENTATION_STATUS: numtests=1
INSTRUMENTATION_STATUS_CODE: 0
OK (1 test)
INSTRUMENTATION_CODE: -1
'@
    $failure = @'
FAILURES!!!
Tests run: 1,  Failures: 1
INSTRUMENTATION_CODE: -1
'@
    $zeroTests = @'
INSTRUMENTATION_STATUS: numtests=0
OK (0 tests)
INSTRUMENTATION_CODE: -1
'@
    Require-Condition (
        (Test-Gate3AndroidInstrumentationSucceeded 0 $success)) `
        'Android instrumentation success fixture was rejected.'
    Require-Condition (
        -not (Test-Gate3AndroidInstrumentationSucceeded 0 $failure)) `
        'Android instrumentation failure fixture was accepted.'
    Require-Condition (
        -not (Test-Gate3AndroidInstrumentationSucceeded 1 $success)) `
        'Android instrumentation process failure was accepted.'
    Require-Condition (
        -not (Test-Gate3AndroidInstrumentationSucceeded 0 $zeroTests)) `
        'Android instrumentation zero-test result was accepted.'

    $fixtureFingerprint = 'A' * 64
    $startInfo = New-AndroidInstrumentationStartInfo `
        'gate3ConnectSendAndDisconnect' 'https://10.0.2.2:43125' `
        'fixture-client' 'fixture-input' $fixtureFingerprint
    $arguments = $startInfo.ArgumentList -join ' '
    Require-Condition (-not $arguments.Contains('credential', [StringComparison]::OrdinalIgnoreCase)) `
        'Android instrumentation command line contains a credential argument.'
    Require-Condition (
        $arguments.Contains("serverPublicKeyFingerprint $fixtureFingerprint", [StringComparison]::Ordinal)) `
        'Android instrumentation command line omits the pinned server fingerprint.'

    $credentialStartInfo = New-Gate3CredentialTransferStartInfo 'fixture-emulator'
    $credentialArguments = @($credentialStartInfo.ArgumentList)
    $expectedCredentialArguments = @(
        '-s', 'fixture-emulator',
        'exec-in', 'run-as', 'dev.beacon.android',
        'tee', 'files/beacon-gate3-client-credential')
    Require-Condition (
        @(Compare-Object $expectedCredentialArguments $credentialArguments -SyncWindow 0).Count -eq 0) `
        'Private credential transfer does not execute the write as the Beacon app UID.'
}

function Assert-OwnedProcessJobFixture() {
    $job = [Beacon.Gate3.OwnedProcessJob]::new()
    $process = $null
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.ArgumentList.Add('-NoProfile')
        $startInfo.ArgumentList.Add('-Command')
        $startInfo.ArgumentList.Add(
            '$wait = [System.Threading.ManualResetEvent]::new($false); [void]$wait.WaitOne()')
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        Require-Condition $process.Start() 'Could not start the Gate 3 job fixture process.'
        $job.Assign($process)
        $job.Dispose()
        $job = $null
        $process.WaitForExit()
        Require-Condition $process.HasExited 'Gate 3 owned-process job did not terminate its child.'
    }
    finally {
        if ($null -ne $job) {
            $job.Dispose()
        }
        if ($null -ne $process) {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $process.WaitForExit()
            }
            $process.Dispose()
        }
    }
}

function Assert-ExactProcessStopFixture() {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-NoProfile')
    $startInfo.ArgumentList.Add('-Command')
    $startInfo.ArgumentList.Add(
        '$wait = [System.Threading.ManualResetEvent]::new($false); [void]$wait.WaitOne()')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Require-Condition $process.Start() 'Could not start the exact-process fixture.'
        [void]$process.Handle
        Stop-ExactProcessAndWait $process
        Require-Condition $process.HasExited 'Exact-process stop did not wait for process exit.'
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

function Assert-ServerOutputCaptureFixture() {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('-NoProfile')
    $startInfo.ArgumentList.Add('-Command')
    $startInfo.ArgumentList.Add(@'
[Console]::Error.WriteLine('fixture-error')
[Console]::Out.WriteLine('{"EventId":14,"State":{"address":"http://0.0.0.0:43125"}}')
$wait = [System.Threading.ManualResetEvent]::new($false)
[void]$wait.WaitOne()
'@)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $capture = [Beacon.Gate3.ProcessOutputCapture]::new()
    try {
        Require-Condition $process.Start() 'Could not start the Server output-capture fixture.'
        $capture.Attach($process)
        Require-Condition (
            $capture.WaitForListeningAddress() -eq 'http://0.0.0.0:43125') `
            'Server output capture did not observe the structured listening event.'
        $capture.WaitForStandardError()
        $process.Kill($true)
        $process.WaitForExit()
        $capture.WaitForDrain()
        Require-Condition ($capture.StandardError.Contains('fixture-error')) `
            'Server output capture did not drain stderr.'
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $capture.Dispose()
        $process.Dispose()
    }
}

function Assert-UnstartedServerCleanupFixture() {
    $process = [Diagnostics.Process]::new()
    Stop-ServerProcess $process $false $null
}

function New-AndroidInstrumentationStartInfo(
    [string]$Method,
    [string]$ServerUrl,
    [string]$ClientId,
    [string]$InputMarker,
    [string]$ServerPublicKeyFingerprint) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'adb'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '-s', $Serial,
        'shell', 'am', 'instrument', '-w', '-r',
        '-e', 'class', "dev.beacon.android.BeaconStreamCoreInstrumentationTest#$Method",
        '-e', 'serverUrl', $ServerUrl,
        '-e', 'clientId', $ClientId,
        '-e', 'inputMarker', $InputMarker,
        '-e', 'serverPublicKeyFingerprint', $ServerPublicKeyFingerprint,
        'dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner')) {
        $startInfo.ArgumentList.Add($argument)
    }
    return $startInfo
}

function Start-AndroidInstrumentation(
    [string]$Method,
    [string]$ServerUrl,
    [string]$ClientId,
    [string]$InputMarker,
    [string]$ServerPublicKeyFingerprint) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = New-AndroidInstrumentationStartInfo `
        $Method $ServerUrl $ClientId $InputMarker $ServerPublicKeyFingerprint
    Require-Condition $process.Start() "Could not start instrumentation method '$Method'."
    return [PSCustomObject]@{
        Method = $Method
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
        Completed = $false
    }
}

function Complete-AndroidInstrumentation($Invocation) {
    try {
        $Invocation.Process.WaitForExit()
        $standardOutput = $Invocation.StandardOutput.GetAwaiter().GetResult()
        $standardError = $Invocation.StandardError.GetAwaiter().GetResult()
        $rendered = @($standardOutput, $standardError) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Join-String -Separator ([Environment]::NewLine)
        if (-not (Test-Gate3AndroidInstrumentationSucceeded $Invocation.Process.ExitCode $rendered)) {
            throw "Instrumentation method '$($Invocation.Method)' failed.`n$rendered"
        }
        return $rendered
    }
    finally {
        $Invocation.Completed = $true
        $Invocation.Process.Dispose()
    }
}

function Start-AndroidGate3Logcat() {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'adb'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-s', $Serial, 'logcat', '-v', 'raw', '-s', 'BeaconGate3:I', '*:S')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    Require-Condition $process.Start() 'Could not start the Gate 3 Android log event reader.'
    return [PSCustomObject]@{
        Process = $process
        StandardError = $process.StandardError.ReadToEndAsync()
    }
}

function Wait-AndroidGate3Marker($Logcat, $Invocation, [string]$Marker) {
    $instrumentationExit = $Invocation.Process.WaitForExitAsync()
    while ($true) {
        $lineTask = $Logcat.Process.StandardOutput.ReadLineAsync()
        $completed = [Threading.Tasks.Task]::WhenAny(
            [Threading.Tasks.Task[]]@($lineTask, $instrumentationExit)).GetAwaiter().GetResult()
        if ($lineTask.IsCompletedSuccessfully) {
            $line = $lineTask.GetAwaiter().GetResult()
            if ($null -eq $line) {
                throw 'The Gate 3 Android log event reader exited before the required marker.'
            }
            if ($line.Contains($Marker, [StringComparison]::Ordinal)) {
                return $true
            }
        }
        if ([object]::ReferenceEquals($completed, $instrumentationExit)) {
            return $false
        }
    }
}

function Stop-AndroidGate3Logcat($Logcat) {
    try {
        if (-not $Logcat.Process.HasExited) {
            $Logcat.Process.Kill()
            $Logcat.Process.WaitForExit()
        }
        [void]$Logcat.StandardError.GetAwaiter().GetResult()
    }
    finally {
        $Logcat.Process.Dispose()
    }
}

function Stop-ExactProcessAndWait([Diagnostics.Process]$Process) {
    Require-Condition (-not $Process.HasExited) 'The exact owned process already exited.'
    $Process.Kill()
    $Process.WaitForExit()
}

function Stop-AndroidInstrumentation($Invocation) {
    if ($Invocation.Completed) {
        return
    }
    try {
        if (-not $Invocation.Process.HasExited) {
            $Invocation.Process.Kill($true)
        }
        $Invocation.Process.WaitForExit()
        [void]$Invocation.StandardOutput.GetAwaiter().GetResult()
        [void]$Invocation.StandardError.GetAwaiter().GetResult()
    }
    finally {
        $Invocation.Completed = $true
        $Invocation.Process.Dispose()
    }
}

function Stop-ServerProcess(
    [Diagnostics.Process]$Process,
    [bool]$Started,
    $Capture) {
    try {
        if ($Started) {
            if (-not $Process.HasExited) {
                $Process.Kill($true)
            }
            $Process.WaitForExit()
            if ($null -ne $Capture) {
                $Capture.WaitForDrain()
            }
        }
    }
    finally {
        if ($null -ne $Capture) {
            $Capture.Dispose()
        }
        $Process.Dispose()
    }
}

function Invoke-AndroidInstrumentation(
    [string]$Method,
    [string]$ServerUrl,
    [string]$ClientId,
    [string]$InputMarker,
    [string]$ServerPublicKeyFingerprint) {
    return Complete-AndroidInstrumentation (
        Start-AndroidInstrumentation `
            $Method $ServerUrl $ClientId $InputMarker $ServerPublicKeyFingerprint)
}

function Invoke-ServerJson(
    [System.Net.Http.HttpClient]$HttpClient,
    [System.Net.Http.HttpMethod]$Method,
    [string]$Path,
    [object]$Body = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new($Method, $Path)
    if ($Method -ne [System.Net.Http.HttpMethod]::Get) {
        $json = if ($null -eq $Body) { '{}' } else { $Body | ConvertTo-Json -Depth 8 -Compress }
        $request.Content = [System.Net.Http.StringContent]::new(
            $json, [System.Text.Encoding]::UTF8, 'application/json')
    }
    try {
        $response = $HttpClient.Send($request)
        if (-not $response.IsSuccessStatusCode) {
            throw "Server request '$Path' did not succeed."
        }
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return $content | ConvertFrom-Json
    }
    finally {
        $request.Dispose()
    }
}

function New-Gate3ClientCredential(
    [string]$CredentialPath,
    [string]$ClientId) {
    $credentialBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $material = [byte[]]::new($salt.Length + $credentialBytes.Length)
    $hash = $null
    try {
        [Array]::Copy($salt, 0, $material, 0, $salt.Length)
        [Array]::Copy(
            $credentialBytes, 0, $material, $salt.Length, $credentialBytes.Length)
        $hash = [Security.Cryptography.SHA256]::HashData($material)
        ConvertTo-Json -InputObject @(@{
            clientId = $ClientId
            salt = [Convert]::ToBase64String($salt)
            hash = [Convert]::ToBase64String($hash)
            revoked = $false
        }) | Set-Content -LiteralPath $CredentialPath -Encoding utf8NoBOM
        return [Convert]::ToBase64String($credentialBytes)
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($credentialBytes)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($salt)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($material)
        if ($null -ne $hash) {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($hash)
        }
    }
}

function Set-Gate3ClientCredential(
    [string]$AndroidSerial,
    [string]$Credential) {
    & adb -s $AndroidSerial shell run-as dev.beacon.android mkdir -p files | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not prepare private Gate 3 credential storage.'
    }

    $startInfo = New-Gate3CredentialTransferStartInfo $AndroidSerial

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Require-Condition $process.Start() 'Could not start private credential transfer.'
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($Credential)
        $process.StandardInput.Close()
        $process.WaitForExit()
        [void]$standardOutput.GetAwaiter().GetResult()
        $errorText = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Could not transfer the private Gate 3 credential.`n$errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-Gate3CredentialTransferStartInfo([string]$AndroidSerial) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'adb'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '-s', $AndroidSerial,
        'exec-in', 'run-as', 'dev.beacon.android',
        'tee', 'files/beacon-gate3-client-credential')) {
        $startInfo.ArgumentList.Add($argument)
    }
    return $startInfo
}

function Remove-Gate3ClientCredentialEvidence(
    [string]$AndroidSerial,
    [switch]$BestEffort) {
    & adb -s $AndroidSerial shell run-as dev.beacon.android `
        rm -f files/beacon-gate3-client-credential 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $BestEffort) {
        throw 'Could not delete private Gate 3 credential evidence.'
    }
}

function Get-Gate3CertificatePublicKeyFingerprint(
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey(
        $Certificate)
    try {
        Require-Condition ($null -ne $publicKey) 'Gate 3 identity has no RSA public key.'
        $subjectPublicKeyInfo = $publicKey.ExportSubjectPublicKeyInfo()
        try {
            return [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($subjectPublicKeyInfo))
        }
        finally {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($subjectPublicKeyInfo)
        }
    }
    finally {
        if ($null -ne $publicKey) {
            $publicKey.Dispose()
        }
    }
}

function Get-Gate3PublicKeyFingerprint([string]$IdentityPath) {
    $certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile(
        $IdentityPath,
        $null,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        return Get-Gate3CertificatePublicKeyFingerprint $certificate
    }
    finally {
        $certificate.Dispose()
    }
}

function Get-Gate3PrivateKeyEvidence([string]$IdentityPath) {
    $pfx = [IO.File]::ReadAllBytes($IdentityPath)
    $certificate = $null
    $privateKey = $null
    $pkcs8 = $null
    try {
        $certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile(
            $IdentityPath,
            $null,
            [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
                [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        $privateKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey(
            $certificate)
        Require-Condition ($null -ne $privateKey) 'Gate 3 identity has no RSA private key.'
        $pkcs8 = $privateKey.ExportPkcs8PrivateKey()
        return @(
            [Convert]::ToBase64String($pfx),
            [Convert]::ToBase64String($pkcs8))
    }
    finally {
        if ($null -ne $pkcs8) {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($pkcs8)
        }
        if ($null -ne $privateKey) {
            $privateKey.Dispose()
        }
        if ($null -ne $certificate) {
            $certificate.Dispose()
        }
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($pfx)
    }
}

function Get-Gate3StreamTicketEvidence([string]$AndroidSerial) {
    try {
        $rendered = (& adb -s $AndroidSerial exec-out run-as dev.beacon.android `
            cat files/beacon-gate3-ticket-evidence 2>$null) -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not read private Gate 3 ticket evidence.'
        }
        $tickets = @(
            $rendered -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $uniqueTickets = @($tickets | Sort-Object -Unique)
        Require-Condition (
            $tickets.Count -eq 3 -and $uniqueTickets.Count -eq 3) `
            'Gate 3 did not capture three unique real-session tickets.'
        return $tickets
    }
    finally {
        Remove-Gate3StreamTicketEvidence $AndroidSerial
    }
}

function Remove-Gate3StreamTicketEvidence(
    [string]$AndroidSerial,
    [switch]$BestEffort) {
    & adb -s $AndroidSerial shell run-as dev.beacon.android `
        rm -f files/beacon-gate3-ticket-evidence 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $BestEffort) {
        throw 'Could not delete private Gate 3 ticket evidence.'
    }
}

function Add-Gate3JournalEvidence(
    [Collections.Generic.List[object]]$Journal,
    [Collections.Generic.List[object]]$WorkerDiagnostics,
    $Snapshot) {
    foreach ($entry in @($Snapshot.diagnostics)) {
        $Journal.Add($entry)
        if ([string]$entry.operation -like 'worker.*') {
            $WorkerDiagnostics.Add($entry)
        }
    }
}

function Write-Gate3Evidence(
    [string]$Directory,
    [string]$ServerOutput,
    [Collections.Generic.List[string]]$Instrumentation,
    [Collections.Generic.List[object]]$Journal,
    [Collections.Generic.List[object]]$WorkerDiagnostics,
    [string]$Logcat,
    [hashtable]$Canaries) {
    if ([string]::IsNullOrWhiteSpace($Directory)) {
        return
    }

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Directory 'server-output.log') `
        -Value $ServerOutput -Encoding utf8NoBOM
    @($WorkerDiagnostics) | ConvertTo-Json -Depth 12 | Set-Content `
        -LiteralPath (Join-Path $Directory 'worker-diagnostics.json') -Encoding utf8NoBOM
    @($Journal) | ConvertTo-Json -Depth 12 | Set-Content `
        -LiteralPath (Join-Path $Directory 'diagnostic-journal.json') -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $Directory 'instrumentation.log') `
        -Value ($Instrumentation -join [Environment]::NewLine) -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $Directory 'logcat.log') `
        -Value $Logcat -Encoding utf8NoBOM
    $Canaries | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $Directory 'canaries.json') -Encoding utf8NoBOM
}

if ($ValidateKestrelParser) {
    Write-Gate3Stage 'parser-fixture'
    Assert-KestrelListeningFixture
    Assert-AndroidInstrumentationFixture
    Assert-OwnedProcessJobFixture
    Assert-ExactProcessStopFixture
    Assert-ServerOutputCaptureFixture
    Assert-UnstartedServerCleanupFixture
    Write-Output 'BEACON_GATE3_KESTREL_PARSER_OK'
    exit 0
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repositoryRoot `
    'tests\Beacon.Server.TestHost\Beacon.Server.TestHost.csproj'
$workerPath = Join-Path $repositoryRoot `
    'native\out\build\windows-x64\Beacon.StreamWorker\Debug\Beacon.StreamWorker.exe'
$serverDll = Join-Path $repositoryRoot `
    'tests\Beacon.Server.TestHost\bin\Debug\net10.0-windows\Beacon.Server.TestHost.dll'
$appApk = Join-Path $repositoryRoot 'src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk'
$testApk = Join-Path $repositoryRoot `
    'src\Beacon.Android\app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk'
$ownedRoot = Join-Path ([IO.Path]::GetTempPath()) "beacon-gate3-$([Guid]::NewGuid().ToString('N'))"
$server = $null
$serverStarted = $false
$serverJob = $null
$serverCapture = $null
$serverAddress = $null
$instrumentationEvidence = [Collections.Generic.List[string]]::new()
$journalEvidence = [Collections.Generic.List[object]]::new()
$workerDiagnosticEvidence = [Collections.Generic.List[object]]::new()
$logcatEvidence = ''
$serverOutputEvidence = ''
$runCanaryId = [Guid]::NewGuid().ToString('N')
$clientId = "gate3-emulator-$runCanaryId"
$credentialPath = Join-Path $ownedRoot 'credentials.json'
$credentialCanary = $null
$privateKeyPathMarker = "BEACON-G3-PRIVATE-KEY-PATH-$runCanaryId"
$identityPath = Join-Path $ownedRoot "$privateKeyPathMarker.pfx"
$privateKeyCanary = @()
$inputPayloadCanary = "BEACON-G3-INPUT-$runCanaryId"
$streamTicketCanary = @()
$evidenceReady = $false

try {
    Write-Gate3Stage 'parser-fixture'
    Assert-KestrelListeningFixture
    Assert-AndroidInstrumentationFixture
    Assert-OwnedProcessJobFixture
    Assert-ExactProcessStopFixture
    Assert-ServerOutputCaptureFixture
    Assert-UnstartedServerCleanupFixture

    New-Item -ItemType Directory -Path $ownedRoot | Out-Null
    $credentialCanary = New-Gate3ClientCredential $credentialPath $clientId

    if (-not $ArtifactsReady) {
        Write-Gate3Stage 'managed-build'
        & dotnet build $serverProject --configuration Debug --warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Beacon.Server.TestHost build failed.' }
        Write-Gate3Stage 'native-build'
        & (Join-Path $repositoryRoot 'scripts\build-native-windows.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Beacon.StreamWorker build failed.' }
        Write-Gate3Stage 'android-build'
        & (Join-Path $repositoryRoot 'scripts\test-android.ps1') `
            -Tasks @('assembleDebug', 'assembleDebugAndroidTest')
        if ($LASTEXITCODE -ne 0) { throw 'Beacon Android APK build failed.' }
    }

    foreach ($path in @($workerPath, $serverDll, $appApk, $testApk)) {
        Require-Condition (Test-Path -LiteralPath $path -PathType Leaf) 'Expected Gate 3 artifact is unavailable.'
    }

    Write-Gate3Stage 'apk-install'
    & adb -s $Serial get-state | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Android target '$Serial' is not online." }
    & adb -s $Serial install -r $appApk | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the Beacon APK.' }
    & adb -s $Serial install -r $testApk | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the Beacon instrumentation APK.' }
    & adb -s $Serial shell pm clear dev.beacon.android | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not clear the owned Beacon app state.' }
    Set-Gate3ClientCredential $Serial $credentialCanary
    & adb -s $Serial logcat -c
    if ($LASTEXITCODE -ne 0) { throw 'Could not clear the emulator log buffer.' }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = Split-Path -Parent $serverDll
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add($serverDll)
    $startInfo.ArgumentList.Add('--urls')
    $startInfo.ArgumentList.Add('https://0.0.0.0:0')
    $startInfo.Environment['Beacon__TestHost__UseProductionStreamWorker'] = 'true'
    $startInfo.Environment['Beacon__Streaming__WorkerPath'] = $workerPath
    $startInfo.Environment['Beacon__Security__TestHost'] = 'true'
    $startInfo.Environment['Beacon__Security__IdentityPath'] = $identityPath
    $startInfo.Environment['Beacon__Security__CredentialsPath'] = $credentialPath
    $startInfo.Environment['Beacon__Profiles__Path'] = Join-Path $ownedRoot 'profiles.json'
    $startInfo.Environment['Logging__Console__FormatterName'] = 'json'
    $startInfo.Environment['Logging__LogLevel__Default'] = 'Information'
    $startInfo.Environment['Logging__LogLevel__Microsoft.AspNetCore'] = 'Warning'

    $server = [Diagnostics.Process]::new()
    $server.StartInfo = $startInfo
    $serverJob = [Beacon.Gate3.OwnedProcessJob]::new()
    Write-Gate3Stage 'server-start'
    $serverStarted = $server.Start()
    Require-Condition $serverStarted 'Could not start Beacon.Server.'
    $serverJob.Assign($server)
    $serverCapture = [Beacon.Gate3.ProcessOutputCapture]::new()
    $serverCapture.Attach($server)
    $serverAddress = $serverCapture.WaitForListeningAddress()
    $listeningUri = [Uri]$serverAddress
    Require-Condition ($listeningUri.Port -gt 0) 'Kestrel did not select an endpoint port.'
    $emulatorServerUrl = "https://10.0.2.2:$($listeningUri.Port)"
    $hostServerUrl = Get-HostServerUrl $listeningUri
    $serverPublicKeyFingerprint = Get-Gate3PublicKeyFingerprint $identityPath
    $privateKeyCanary = @(
        $privateKeyPathMarker
        Get-Gate3PrivateKeyEvidence $identityPath)

    $httpHandler = [System.Net.Http.HttpClientHandler]::new()
    $httpHandler.ServerCertificateCustomValidationCallback =
        [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
    $httpClient.BaseAddress = [Uri]$hostServerUrl
    $httpClient.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    try {
        $serverIdentity = Invoke-ServerJson `
            $httpClient ([System.Net.Http.HttpMethod]::Get) '/identity'
        Require-Condition (
            [string]$serverIdentity.publicKeyFingerprint -eq $serverPublicKeyFingerprint) `
            'The localhost TestHost identity does not match its generated certificate.'
        if (-not $SessionOnly) {
            Write-Gate3Stage 'gate4-network-change-instrumentation'
            $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
                'gate4DefaultNetworkChangeMonitor' $emulatorServerUrl $clientId `
                $inputPayloadCanary $serverPublicKeyFingerprint))
        }
        Write-Gate3Stage 'gate4-certified-benchmark-instrumentation'
        $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
            'gate4CertifiedBenchmarkEvidence' $emulatorServerUrl $clientId `
            $inputPayloadCanary $serverPublicKeyFingerprint))
        Write-Gate3Stage 'gate4-session-preflight-instrumentation'
        $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
            'gate4CertifiedSessionPreflight' $emulatorServerUrl $clientId `
            $inputPayloadCanary $serverPublicKeyFingerprint))
        if (-not $SessionOnly) {
            Write-Gate3Stage 'gate4-real-hardware-instrumentation'
            $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
                'gate4NetworkAndHardwareBenchmark' $emulatorServerUrl $clientId `
                $inputPayloadCanary $serverPublicKeyFingerprint))
        }
        Write-Gate3Stage 'first-instrumentation'
        $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
            'gate3ConnectSendAndDisconnect' $emulatorServerUrl $clientId `
            $inputPayloadCanary $serverPublicKeyFingerprint))
        Write-Gate3Stage 'reconnect-instrumentation'
        $instrumentationEvidence.Add((Invoke-AndroidInstrumentation `
            'gate3ReconnectAndStop' $emulatorServerUrl $clientId `
            $inputPayloadCanary $serverPublicKeyFingerprint))

        Write-Gate3Stage 'session-evidence'
        $firstSnapshot = Invoke-ServerJson $httpClient ([System.Net.Http.HttpMethod]::Get) '/admin/snapshot'
        Add-Gate3JournalEvidence $journalEvidence $workerDiagnosticEvidence $firstSnapshot
        $clientSnapshot = @(
            $firstSnapshot.clients |
                Where-Object { $_.clientId -eq $clientId })
        Require-Condition ($clientSnapshot.Count -eq 1) `
            'The Gate 4 benchmark client snapshot was not retained.'
        $completedBenchmarks = @(
            $clientSnapshot[0].benchmarks |
                Where-Object {
                    $null -ne $_.completedAt -and $null -ne $_.selectedResult
                })
        $pendingBenchmarks = @(
            $clientSnapshot[0].benchmarks |
                Where-Object {
                    $null -eq $_.completedAt -or $null -eq $_.selectedResult
                })
        Require-Condition ($completedBenchmarks.Count -eq 2) `
            'The expected benchmark evidence was not retained.'
        Require-Condition (@(
            $completedBenchmarks |
                Where-Object { $_.trigger -eq 'SessionPreflight' }).Count -eq 1) `
            'Gate 4 did not retain exactly one completed session preflight.'
        Require-Condition ($pendingBenchmarks.Count -eq 0) `
            'The real Gate 4 observation left an orphaned benchmark run.'
        $operations = @($firstSnapshot.diagnostics | ForEach-Object { $_.operation })
        foreach ($operation in @(
            'worker.connection_observed',
            'worker.connection_configured',
            'worker.transport_connected',
            'worker.transport_authenticated',
            'worker.media',
            'input.forwarded',
            'worker.feedback',
            'worker.transport_disconnected')) {
            Require-Condition ($operations -contains $operation) 'The Gate 3 session did not publish required Worker evidence.'
        }
        $mediaMarkers = @(
            $firstSnapshot.diagnostics |
                Where-Object { $_.operation -eq 'worker.media' } |
                ForEach-Object {
                    '{0}:{1}' -f `
                        [long]$_.metadata.workerSessionGeneration, `
                        [long]$_.metadata.sequence
                } |
                Sort-Object -Unique)
        Require-Condition (($mediaMarkers -join ',') -eq '1:1,2:1') `
            'The reconnect did not emit a fresh generation-local Worker media sequence.'

        $logcat = (& adb -s $Serial logcat -d -v raw -s BeaconGate3:I) -join [Environment]::NewLine
        $requiredMarkers = @(
            'BEACON_GATE3_READY',
            'BEACON_GATE3_FRAME 1',
            'BEACON_GATE3_INPUT_ECHO 1',
            'BEACON_GATE3_FEEDBACK 1',
            'BEACON_GATE3_RECONNECT_FRESH_TICKET')
        if (-not $SessionOnly) {
            $requiredMarkers += @(
                'BEACON_GATE4_CHANGE_MONITOR',
                'BEACON_GATE4_REAL_HARDWARE_OBSERVED')
        }
        $requiredMarkers += @(
            'BEACON_GATE4_BENCHMARK_COMPLETE',
            'BEACON_GATE4_SESSION_PREFLIGHT')
        foreach ($marker in $requiredMarkers) {
            Require-Condition ($logcat -match [Regex]::Escape($marker)) 'Required Android Gate 3 evidence was not emitted.'
            Write-Output $marker
        }

        Write-Gate3Stage 'active-worker-crash-instrumentation'
        $crashLogcat = $null
        $crashInvocation = $null
        $workerProcess = $null
        try {
            $crashLogcat = Start-AndroidGate3Logcat
            $crashInvocation = Start-AndroidInstrumentation `
                'gate3ConnectAndAwaitWorkerCrash' $emulatorServerUrl $clientId `
                $inputPayloadCanary $serverPublicKeyFingerprint
            $armed = Wait-AndroidGate3Marker `
                $crashLogcat $crashInvocation 'BEACON_GATE3_WORKER_CRASH_ARMED'
            if (-not $armed) {
                [void](Complete-AndroidInstrumentation $crashInvocation)
                throw 'Android instrumentation exited without arming an active Worker crash.'
            }

            $beforeCrash = Invoke-ServerJson `
                $httpClient ([System.Net.Http.HttpMethod]::Get) '/admin/snapshot'
            $activeRuntime = @(
                $beforeCrash.streams |
                    Where-Object { $_.clientId -eq $clientId -and $_.state -eq 'running' })
            Require-Condition ($activeRuntime.Count -eq 1) `
                'Worker crash instrumentation did not retain exactly one active runtime.'
            $activeMediaMarkers = @(
                $beforeCrash.diagnostics |
                    Where-Object { $_.operation -eq 'worker.media' } |
                    ForEach-Object {
                        '{0}:{1}' -f `
                            [long]$_.metadata.workerSessionGeneration, `
                            [long]$_.metadata.sequence
                    } |
                    Sort-Object -Unique)
            Require-Condition ($activeMediaMarkers -contains '3:1') `
                'Worker crash instrumentation did not receive its active media marker.'

            $workers = @(
                Get-CimInstance Win32_Process -Filter "ParentProcessId = $($server.Id)" |
                    Where-Object { $_.Name -eq 'Beacon.StreamWorker.exe' })
            Require-Condition ($workers.Count -eq 1) `
                'Beacon.Server did not own exactly one Worker child.'
            $workerProcess = [Diagnostics.Process]::GetProcessById($workers[0].ProcessId)
            [void]$workerProcess.Handle
            $currentWorker = Get-CimInstance Win32_Process `
                -Filter "ProcessId = $($workerProcess.Id)"
            Require-Condition (
                $null -ne $currentWorker -and
                $currentWorker.ParentProcessId -eq $server.Id -and
                $currentWorker.Name -eq 'Beacon.StreamWorker.exe' -and
                [IO.Path]::GetFullPath($workerProcess.MainModule.FileName) -eq
                    [IO.Path]::GetFullPath($workerPath)) `
                'The retained Worker process identity changed before termination.'
            Write-Gate3Stage 'worker-crash'
            Stop-ExactProcessAndWait $workerProcess
            $instrumentationEvidence.Add((Complete-AndroidInstrumentation $crashInvocation))
            $crashInvocation = $null
        }
        finally {
            if ($null -ne $workerProcess) {
                $workerProcess.Dispose()
            }
            if ($null -ne $crashInvocation -and -not $crashInvocation.Completed) {
                Stop-AndroidInstrumentation $crashInvocation
            }
            if ($null -ne $crashLogcat) {
                Stop-AndroidGate3Logcat $crashLogcat
            }
        }

        $crashLog = (& adb -s $Serial logcat -d -v raw -s BeaconGate3:I) -join `
            [Environment]::NewLine
        Require-Condition (
            $crashLog -match [Regex]::Escape('BEACON_GATE3_WORKER_CRASH_OBSERVED')) `
            'Android StreamCore did not observe the active Worker connection loss.'

        [void](Invoke-ServerJson $httpClient ([System.Net.Http.HttpMethod]::Get) '/health')
        $afterCrash = Invoke-ServerJson $httpClient ([System.Net.Http.HttpMethod]::Get) '/admin/snapshot'
        Add-Gate3JournalEvidence $journalEvidence $workerDiagnosticEvidence $afterCrash
        $invalidated = @($afterCrash.streams | Where-Object { $_.clientId -eq $clientId })
        Require-Condition ($invalidated.Count -eq 0) 'Worker crash did not invalidate the active streaming runtime.'
        Write-Output 'BEACON_GATE3_WORKER_CRASH_ISOLATED'

        Write-Gate3Stage 'emergency-restore'
        $restore = Invoke-ServerJson $httpClient ([System.Net.Http.HttpMethod]::Post) "/clients/$clientId/emergency-restore"
        Require-Condition ([bool]$restore.recovered) 'Emergency restore did not succeed after Worker isolation.'
        Write-Output 'BEACON_GATE3_EMERGENCY_RESTORE_OK'

        Write-Gate3Stage 'real-secret-evidence'
        $streamTicketCanary = @(Get-Gate3StreamTicketEvidence $Serial)
        $finalSnapshot = Invoke-ServerJson `
            $httpClient ([System.Net.Http.HttpMethod]::Get) '/admin/snapshot'
        Add-Gate3JournalEvidence $journalEvidence $workerDiagnosticEvidence $finalSnapshot
        $logcatEvidence = (& adb -s $Serial logcat -d -v raw) -join [Environment]::NewLine
        $evidenceReady = $true
    }
    catch {
        $instrumentationEvidence.Add($_.Exception.ToString())
        try {
            $failureSnapshot = Invoke-ServerJson `
                $httpClient ([System.Net.Http.HttpMethod]::Get) '/admin/snapshot'
            Add-Gate3JournalEvidence `
                $journalEvidence $workerDiagnosticEvidence $failureSnapshot
        }
        catch {
            $workerDiagnosticEvidence.Add([pscustomobject]@{
                operation = 'failure-evidence.snapshot'
                message = $_.Exception.Message
            })
        }
        try {
            $logcatEvidence = (& adb -s $Serial logcat -d -v raw) -join `
                [Environment]::NewLine
        }
        catch {
            $logcatEvidence = "Could not capture Android logcat: $($_.Exception.Message)"
        }
        if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
            Write-Gate3Evidence `
                $EvidenceDirectory `
                $serverOutputEvidence `
                $instrumentationEvidence `
                $journalEvidence `
                $workerDiagnosticEvidence `
                $logcatEvidence `
                @{ failure = $true }
        }
        throw
    }
    finally {
        if ($null -ne $httpClient) {
            $httpClient.Dispose()
        }
    }
}
finally {
    try {
        if ($null -ne $serverJob) {
            $serverJob.Dispose()
        }
        if ($null -ne $server) {
            $completedCapture = $serverCapture
            Stop-ServerProcess $server $serverStarted $serverCapture
            if ($null -ne $completedCapture) {
                $serverOutputEvidence = @(
                    $completedCapture.StandardOutput,
                    $completedCapture.StandardError
                ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Join-String -Separator ([Environment]::NewLine)
            }
            $server = $null
            $serverCapture = $null
        }
        if (-not $evidenceReady -and
            -not [string]::IsNullOrWhiteSpace($serverOutputEvidence)) {
            [Console]::Error.WriteLine('--- Beacon Gate 3 server failure evidence ---')
            [Console]::Error.WriteLine($serverOutputEvidence)
        }
        if ($evidenceReady -and -not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
            Require-Condition ($streamTicketCanary.Count -eq 3) `
                'Gate 3 real-session ticket evidence is unavailable.'
            Require-Condition (-not [string]::IsNullOrWhiteSpace($credentialCanary)) `
                'Gate 3 client credential evidence is unavailable.'
            Require-Condition ($privateKeyCanary.Count -ge 3) `
                'Gate 3 private-key evidence is unavailable.'
            Write-Gate3Evidence `
                $EvidenceDirectory `
                $serverOutputEvidence `
                $instrumentationEvidence `
                $journalEvidence `
                $workerDiagnosticEvidence `
                $logcatEvidence `
                @{
                    stream_ticket = $streamTicketCanary
                    client_credential = $credentialCanary
                    private_key = $privateKeyCanary
                    worker_executable_path = $workerPath
                    input_payload = $inputPayloadCanary
                }
        }
    }
    finally {
        Remove-Gate3ClientCredentialEvidence $Serial -BestEffort
        Remove-Gate3StreamTicketEvidence $Serial -BestEffort
        $temporaryRoot = [IO.Path]::GetFullPath($ownedRoot)
        $temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($temporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $temporaryRoot)) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}
