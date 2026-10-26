using System.Collections.Concurrent;
using System.Text.Json;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

internal sealed class FakeDockRenderer : IDockRenderer
{
    private readonly Dictionary<string, byte> _colors = [];
    public int Version { get; set; }
    public ConcurrentQueue<(DockKey Key, bool Pressed)> Keys { get; } = new();
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<SurfaceImage> RenderKeyAsync(DockKey key, bool pressed, int size)
    {
        Keys.Enqueue((key, pressed));
        Started.TrySetResult();
        if (Gate != null) await Gate.Task;
        string content = key.GetType().Name + JsonSerializer.Serialize(key, key.GetType()) + pressed + Version;
        return Image(content, size);
    }

    public Task<SurfaceImage> RenderDisplayAsync(UsageDisplay? display, int size) =>
        Task.FromResult(Image(JsonSerializer.Serialize(display), size));

    private SurfaceImage Image(string content, int size)
    {
        byte color;
        lock (_colors)
        {
            if (_colors.TryGetValue(content, out color) == false)
            {
                color = checked((byte)(_colors.Count + 1));
                _colors.Add(content, color);
            }
        }
        var pixels = new byte[size * size * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color;
            pixels[index + 3] = 255;
        }
        return new SurfaceImage(size, size, pixels);
    }
}
