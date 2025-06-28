namespace WildernessLabs.Meadow.Hosting;

internal interface IMeadowCliRunner
{
    Task<IEnumerable<string>> ListPortsAsync(CancellationToken cancellationToken);
}

internal sealed class MeadowCliRunner : IMeadowCliRunner
{
    public Task<IEnumerable<MeadowPort>> ListPortsAsync(CancellationToken cancellationToken)
    {
        yield return Task.FromResult<IEnumerable<MeadowPort>>(
            new List<MeadowPort>
            {
                new MeadowPort("COM3", new MeadowDevice("Meadow F7v2 Feather")),
                new MeadowPort("COM4", new MeadowDevice("Meadow F7v2 Feather")),
                new MeadowPort("COM5", new MeadowDevice("Meadow F7v2 Feather"))
            });
    }
}

internal sealed class MeadowPort(string serialPort, MeadowDevice device)
{
    public string SerialPort { get; } = serialPort;
    public MeadowDevice Device { get; } = device;
}

internal sealed class MeadowDevice(string name)
{
    public string Name { get; } = name;
}