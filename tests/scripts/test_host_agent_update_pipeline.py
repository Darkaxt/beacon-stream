import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]


class HostAgentUpdatePipelineTests(unittest.TestCase):
    def read(self, relative_path: str) -> str:
        return (ROOT / relative_path).read_text(encoding="utf-8")

    def test_workflow_is_manual_request_scoped_and_ci_signed(self) -> None:
        workflow = self.read(".github/workflows/host-agent-update.yml")

        self.assertIn("workflow_dispatch:", workflow)
        self.assertIn("push:", workflow)
        self.assertIn("github.run_id", workflow)
        self.assertIn("request_id:", workflow)
        self.assertIn("BEACON_HOST_AGENT_UPDATE_SIGNING_KEY_PEM_B64", workflow)
        self.assertIn("github.sha", workflow)
        self.assertIn("actions/upload-artifact", workflow)
        self.assertNotIn("timeout-minutes", workflow)

    def test_workflow_rebuilds_for_every_packaged_runtime_dependency(self) -> None:
        workflow = self.read(".github/workflows/host-agent-update.yml")

        required_paths = (
            "Directory.Build.props",
            "global.json",
            "contracts/**",
            "src/Beacon.Core/**",
            "src/Beacon.Platform.Windows/**",
            "src/Beacon.StreamWorker.Contracts/**",
        )
        for required_path in required_paths:
            with self.subTest(required_path=required_path):
                self.assertIn(f"- '{required_path}'", workflow)
        self.assertIn(
            "dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj",
            workflow,
        )

    def test_unattended_updater_has_no_uac_or_protected_write_fallback(self) -> None:
        updater = self.read("scripts/update-host-agent.ps1").lower()

        self.assertIn("gh workflow run", updater)
        self.assertIn("beacon.hostagent.control", updater)
        self.assertIn("hostagenttransactions", updater)
        self.assertNotIn("runas", updater)
        self.assertNotIn("programfiles", updater)
        self.assertNotIn("start-process", updater)
        self.assertNotIn("timeout", updater)

    def test_installer_migrates_task_to_stable_bootstrap(self) -> None:
        installer = self.read("scripts/install-host-agent.ps1")

        self.assertIn("Beacon.HostAgent.Bootstrap.exe", installer)
        self.assertIn('"HostAgent\\Versions"', installer)
        self.assertIn('"State\\current-version.json"', installer)
        self.assertIn("New-ScheduledTaskAction", installer)
        self.assertIn("$installedBootstrap", installer)

    def test_installer_terminates_orphaned_host_agent_before_waiting(self) -> None:
        installer = self.read("scripts/install-host-agent.ps1")

        stop_index = installer.index("Stop-Process")
        wait_index = installer.index("Wait-Process")
        self.assertLess(stop_index, wait_index)

    def test_bootstrap_has_no_time_owned_startup_or_rollback(self) -> None:
        bootstrap = "\n".join(
            path.read_text(encoding="utf-8")
            for path in (ROOT / "src" / "Beacon.HostAgent.Bootstrap").glob("*.cs")
        )

        self.assertNotIn("Task.Delay", bootstrap)
        self.assertNotIn("Thread.Sleep", bootstrap)
        self.assertNotIn("CancelAfter", bootstrap)
        self.assertNotIn("TimeSpan", bootstrap)


if __name__ == "__main__":
    unittest.main()
