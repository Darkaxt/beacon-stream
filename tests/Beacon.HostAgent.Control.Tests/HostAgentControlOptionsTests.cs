namespace Beacon.HostAgent.Control.Tests;

public sealed class HostAgentControlOptionsTests
{
    [Fact]
    public void StatusAcceptsNoAdditionalArguments()
    {
        HostAgentControlOptions options = HostAgentControlOptions.Parse(["status"]);

        Assert.Equal(HostAgentControlCommand.Status, options.Command);
        Assert.Null(options.PackageId);
        Assert.Null(options.TransactionId);
    }

    [Fact]
    public void InstallAcceptsOnlyPackageAndTransactionIdentifiers()
    {
        Guid transactionId = Guid.NewGuid();

        HostAgentControlOptions options = HostAgentControlOptions.Parse([
            "install",
            "--package-id", "agent-0123456789abcdef",
            "--transaction-id", transactionId.ToString("D")
        ]);

        Assert.Equal(HostAgentControlCommand.Install, options.Command);
        Assert.Equal("agent-0123456789abcdef", options.PackageId);
        Assert.Equal(transactionId, options.TransactionId);
    }

    [Fact]
    public void QueryAcceptsOnlyTransactionIdentifier()
    {
        Guid transactionId = Guid.NewGuid();

        HostAgentControlOptions options = HostAgentControlOptions.Parse([
            "query",
            "--transaction-id", transactionId.ToString("D")
        ]);

        Assert.Equal(HostAgentControlCommand.Query, options.Command);
        Assert.Equal(transactionId, options.TransactionId);
    }

    [Theory]
    [InlineData()]
    [InlineData("status", "extra")]
    [InlineData("install", "--package-id", "agent-one")]
    [InlineData("install", "--package-id", "../agent", "--transaction-id", "69dbdaab-d477-4dc9-b92f-ee8846f11ed0")]
    [InlineData("install", "--path", "C:\\malware.exe", "--transaction-id", "69dbdaab-d477-4dc9-b92f-ee8846f11ed0")]
    [InlineData("query", "--command", "calc.exe")]
    [InlineData("unknown")]
    public void UnknownOrIncompleteArgumentsAreRejected(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => HostAgentControlOptions.Parse(arguments));
    }
}
