using DockPad.Services.StreamDock;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Calcul et bornes du processeur, première mesure, réinitialisation et conservation des lectures en échec.
/// Arrondi, grands compteurs et sérialisation d'une mesure avec une réinitialisation concurrente.
/// </summary>
public class SystemLoadMonitorTests
{
    [Fact]
    public void Processeur_CalculeSurLIntervalle()
    {
        var previous = new CpuTimes(Idle: 100, Kernel: 300, User: 100);
        var current = new CpuTimes(Idle: 150, Kernel: 500, User: 200);

        var percent = SystemLoadMonitor.CpuPercent(previous, current);

        Assert.Equal(83, percent);
    }

    [Fact]
    public void Processeur_IntervalleNul_Zero()
    {
        var times = new CpuTimes(Idle: 100, Kernel: 300, User: 100);

        var percent = SystemLoadMonitor.CpuPercent(times, times);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void Processeur_PremiereMesure_PasDeValeur()
    {
        var monitor = new SystemLoadMonitor(
            readCpu: () => new CpuTimes(Idle: 100, Kernel: 300, User: 100),
            readMemory: () => 61);

        var load = monitor.Sample();

        Assert.Null(load.CpuPct);
        Assert.Equal(61, load.MemoryPct);
    }

    [Fact]
    public void Processeur_ApresReinitialisation_PasDeValeur()
    {
        var readings = new Queue<CpuTimes?>(
        [
            new CpuTimes(Idle: 100, Kernel: 300, User: 100),
            new CpuTimes(Idle: 150, Kernel: 500, User: 200),
            new CpuTimes(Idle: 500, Kernel: 1000, User: 500),
            new CpuTimes(Idle: 550, Kernel: 1200, User: 600),
        ]);
        var monitor = new SystemLoadMonitor(readCpu: readings.Dequeue, readMemory: () => null);
        monitor.Sample();
        Assert.Equal(83, monitor.Sample().CpuPct);

        monitor.Reset();

        Assert.Null(monitor.Sample().CpuPct);
        Assert.Equal(83, monitor.Sample().CpuPct);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(null)]
    public void Memoire_LectureEnEchec_GardeLaPrecedente(int? first)
    {
        var readings = new Queue<int?>([first, null]);
        var monitor = new SystemLoadMonitor(readCpu: () => null, readMemory: readings.Dequeue);

        Assert.Equal(first, monitor.Sample().MemoryPct);
        Assert.Equal(first, monitor.Sample().MemoryPct);
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(200, 100, 100, 0)]
    [InlineData(300, 100, 100, 0)]
    [InlineData(-50, 100, 100, 100)]
    [InlineData(0, -50, -50, 0)]
    public void Processeur_BorneEntre0Et100(int idleDelta, int kernelDelta, int userDelta, int expected)
    {
        var previous = new CpuTimes(Idle: 100, Kernel: 100, User: 100);
        var current = new CpuTimes(
            Idle: (ulong)(100 + idleDelta),
            Kernel: (ulong)(100 + kernelDelta),
            User: (ulong)(100 + userDelta));

        var percent = SystemLoadMonitor.CpuPercent(previous, current);

        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(174, 83)]
    [InlineData(175, 83)]
    [InlineData(176, 82)]
    public void Processeur_ArronditAuPlusProche(int idle, int expected)
    {
        var current = new CpuTimes(Idle: (ulong)idle, Kernel: 500, User: 500);

        var percent = SystemLoadMonitor.CpuPercent(default, current);

        Assert.Equal(expected, percent);
    }

    [Fact]
    public void Processeur_GrandsCompteurs_SansDebordement()
    {
        var current = new CpuTimes(Idle: ulong.MaxValue, Kernel: ulong.MaxValue, User: ulong.MaxValue);

        var percent = SystemLoadMonitor.CpuPercent(default, current);

        Assert.Equal(50, percent);
    }

    [Fact]
    public void Processeur_GrandsCompteurs_ConserveLesPetitsIntervalles()
    {
        var previous = new CpuTimes(Idle: ulong.MaxValue - 50, Kernel: ulong.MaxValue - 200, User: ulong.MaxValue - 100);
        var current = new CpuTimes(Idle: ulong.MaxValue, Kernel: ulong.MaxValue, User: ulong.MaxValue);

        var percent = SystemLoadMonitor.CpuPercent(previous, current);

        Assert.Equal(83, percent);
    }

    [Fact]
    public void Processeur_LectureEnEchec_GardeLaPrecedenteEtLaBase()
    {
        var readings = new Queue<CpuTimes?>(
        [
            new CpuTimes(Idle: 100, Kernel: 300, User: 100),
            new CpuTimes(Idle: 150, Kernel: 500, User: 200),
            null,
            new CpuTimes(Idle: 300, Kernel: 700, User: 300),
        ]);
        var monitor = new SystemLoadMonitor(readCpu: readings.Dequeue, readMemory: () => null);

        Assert.Null(monitor.Sample().CpuPct);
        Assert.Equal(83, monitor.Sample().CpuPct);
        Assert.Equal(83, monitor.Sample().CpuPct);
        Assert.Equal(50, monitor.Sample().CpuPct);
    }

    [Fact]
    public void Processeur_PremiereLectureEnEchec_AttendDeuxLecturesReussies()
    {
        var readings = new Queue<CpuTimes?>(
        [
            null,
            new CpuTimes(Idle: 100, Kernel: 300, User: 100),
            new CpuTimes(Idle: 150, Kernel: 500, User: 200),
        ]);
        var monitor = new SystemLoadMonitor(readCpu: readings.Dequeue, readMemory: () => null);

        Assert.Null(monitor.Sample().CpuPct);
        Assert.Null(monitor.Sample().CpuPct);
        Assert.Equal(83, monitor.Sample().CpuPct);
    }

    [Fact]
    public void Reinitialisation_LecturesEnEchec_AucuneAncienneValeur()
    {
        var cpuReadings = new Queue<CpuTimes?>(
        [
            new CpuTimes(Idle: 100, Kernel: 300, User: 100),
            new CpuTimes(Idle: 150, Kernel: 500, User: 200),
            null,
        ]);
        var memoryReadings = new Queue<int?>([61, 62, null]);
        var monitor = new SystemLoadMonitor(readCpu: cpuReadings.Dequeue, readMemory: memoryReadings.Dequeue);
        monitor.Sample();
        Assert.Equal(new SystemLoad(CpuPct: 83, MemoryPct: 62), monitor.Sample());

        monitor.Reset();

        Assert.Equal(new SystemLoad(CpuPct: null, MemoryPct: null), monitor.Sample());
    }

    [Fact]
    public async Task Reinitialisation_PendantUneMesure_OublieLaMesureEnCours()
    {
        using var readingStarted = new ManualResetEventSlim();
        using var releaseReading = new ManualResetEventSlim();
        using var resetStarted = new ManualResetEventSlim();
        var reads = 0;
        var monitor = new SystemLoadMonitor(readCpu: () =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                readingStarted.Set();
                if (releaseReading.Wait(TimeSpan.FromSeconds(5)) == false)
                    throw new TimeoutException("The CPU reading was not released.");
            }
            return new CpuTimes(Idle: 100, Kernel: 300, User: 100);
        }, readMemory: () => 61);
        var sample = Task.Factory.StartNew(() => monitor.Sample(), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task reset = Task.CompletedTask;

        try
        {
            Assert.True(readingStarted.Wait(TimeSpan.FromSeconds(5)));
            reset = Task.Factory.StartNew(() =>
            {
                resetStarted.Set();
                monitor.Reset();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(resetStarted.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(100);
            Assert.False(reset.IsCompleted);
        }
        finally
        {
            releaseReading.Set();
            await Task.WhenAll(sample, reset).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Null(monitor.Sample().CpuPct);
    }

    [Fact]
    public void LecturesWindows_ValeursEntre0Et100()
    {
        var monitor = new SystemLoadMonitor();

        monitor.Sample();
        Thread.Sleep(50);
        var load = monitor.Sample();

        Assert.InRange(load.MemoryPct ?? -1, 0, 100);
        Assert.InRange(load.CpuPct ?? -1, 0, 100);
    }
}
