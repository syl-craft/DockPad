using System.IO;
using System.IO.Pipes;
using System.Text;
using DockPad.Services;

namespace DockPad.Tests;

public class McpPipeServiceTests
{
    private static string NewName() => "DockPad_Test_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Send_RoundTripPreservesLargeResponse()
    {
        var name = NewName();
        var response = new string('x', 200_000) + "é終";
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serving = McpPipeService.RunServerAsync(_ => response, stop.Token, name);
        try
        {
            Assert.Equal(response, await McpPipeService.SendAsync("{}", 5000, name));
            Assert.Equal(response, await McpPipeService.SendAsync("{}", 5000, name));
        }
        finally { stop.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Send_AbsentPipe_TimesOut()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => McpPipeService.SendAsync("{}", 200, NewName()));
    }

    [Fact]
    public async Task Server_ProcessingTimeDoesNotConsumeResponseWriteDeadline()
    {
        var name = NewName();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serving = McpPipeService.RunServerAsync(_ =>
        {
            Thread.Sleep(500); // Traitement plus long que le délai d'IO.
            return "ok";
        }, stop.Token, name, timeoutMs: 200);
        try { Assert.Equal("ok", await McpPipeService.SendAsync("{}", 3000, name)); }
        finally { stop.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Send_ConnectedButSilentServer_TimesOut()
    {
        var name = NewName();
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = server.WaitForConnectionAsync(stop.Token);
        var sending = McpPipeService.SendAsync("{}", 500, name);
        await connected;
        using var reader = new StreamReader(server, leaveOpen: true);
        Assert.Equal("{}", await reader.ReadLineAsync(stop.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => sending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_ReleasesAllSlotsWhenClientsStall(bool sendRequest)
    {
        var name = NewName();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var calls = 0;
        var blockedCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = McpPipeService.RunServerAsync(request =>
        {
            if (request == "blocked")
            {
                if (Interlocked.Increment(ref calls) == 4) blockedCalls.TrySetResult();
                return new string('x', 200_000); // Réponse supérieure à la capacité du buffer.
            }
            return "ok";
        }, stop.Token, name, timeoutMs: 1000);
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                clients.Add(client);
                await client.ConnectAsync(stop.Token);
                if (sendRequest) await client.WriteAsync(Encoding.UTF8.GetBytes("blocked\n"), stop.Token);
            }
            if (sendRequest) await blockedCalls.Task.WaitAsync(stop.Token);
            // Les clients restent connectés sans lire les réponses.
            await Task.Delay(1500, stop.Token);
            Assert.Equal("ok", await McpPipeService.SendAsync("next", 5000, name));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            stop.Cancel();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Server_DoesNotWaitForeverForClientToCloseAfterResponse()
    {
        var name = NewName();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serving = McpPipeService.RunServerAsync(_ => "ok", stop.Token, name, timeoutMs: 500);
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                clients.Add(client);
                await client.ConnectAsync(stop.Token);
                await client.WriteAsync(Encoding.UTF8.GetBytes("{}\n"), stop.Token);
                using var reader = new StreamReader(client, leaveOpen: true);
                Assert.Equal("ok", await reader.ReadLineAsync(stop.Token));
            }
            await Task.Delay(1000, stop.Token);
            Assert.Equal("ok", await McpPipeService.SendAsync("{}", 3000, name));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            stop.Cancel();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task LineServer_SilentClientDoesNotBlockNextRequest()
    {
        var name = NewName();
        var pipe = new LinePipeService(name);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = pipe.RunServerAsync(line => received.TrySetResult(line), stop.Token, timeoutMs: 500);
        using var idle = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        try
        {
            await idle.ConnectAsync(stop.Token);
            await Task.Delay(1000, stop.Token);
            Assert.True(await Task.Run(() => pipe.TrySendSilently("hello", 3000)));
            Assert.Equal("hello", await received.Task.WaitAsync(stop.Token));
        }
        finally { stop.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task LineClient_NonReadingServerDoesNotBlockWriteForever()
    {
        var name = NewName();
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = server.WaitForConnectionAsync(stop.Token);
        var sending = Task.Run(() => new LinePipeService(name).TrySendSilently(new string('x', 200_000), 500));
        await connected;
        Assert.False(await sending.WaitAsync(stop.Token));
    }
}
