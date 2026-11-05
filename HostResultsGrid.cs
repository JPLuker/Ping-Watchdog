namespace PingWatchdog;

// Cell changes paint in one buffer, without erasing the table between ping results.
internal sealed class HostResultsGrid : DataGridView
{
    internal HostResultsGrid() { DoubleBuffered = true; }
}
