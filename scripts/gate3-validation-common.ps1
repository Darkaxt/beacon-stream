function Test-Gate3AndroidInstrumentationSucceeded(
    [int]$ExitCode,
    [string]$Output) {
    return $ExitCode -eq 0 -and
        $Output -match 'INSTRUMENTATION_STATUS:\s+numtests=[1-9][0-9]*' -and
        $Output -match 'INSTRUMENTATION_CODE:\s+-1' -and
        $Output -notmatch 'FAILURES!!!|INSTRUMENTATION_FAILED|INSTRUMENTATION_ABORTED|Process crashed'
}
