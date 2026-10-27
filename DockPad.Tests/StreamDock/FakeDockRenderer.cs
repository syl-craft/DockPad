using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

internal sealed class FakeDockRenderer : IDockRenderer
{
    private readonly Dictionary<string, byte> _colors = [];
    public int Version { get; set; }
    public Exception? SideStripFailure { get; set; }
    public int SideStripVersion { get; set; }
    public ConcurrentQueue<(SideStripContent Content, SideStripStyle Style, SurfaceSize Size)> SideStrips { get; } = new();
    public TaskCompletionSource? SideStripGate { get; set; }
    public TaskCompletionSource SideStripStarted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public async Task<SurfaceImage> RenderSideStripAsync(SideStripContent content, SideStripStyle style, SurfaceSize size)
    {
        var gate = SideStripGate;
        int version = SideStripVersion;
        SideStrips.Enqueue((content, style, size));
        SideStripStarted.TrySetResult();
        if (gate != null) await gate.Task;
        if (SideStripFailure != null) throw SideStripFailure;
        return CreateSideStripImage(content, style, size, version);
    }

    public static SurfaceImage CreateSideStripImage(SideStripContent content, SideStripStyle style, SurfaceSize size, int version)
    {
        var signature = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content) + style + version));
        var pixels = new byte[size.Width * size.Height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = signature[index % signature.Length];
            pixels[index + 1] = signature[(index + 1) % signature.Length];
            pixels[index + 2] = signature[(index + 2) % signature.Length];
            pixels[index + 3] = 255;
        }
        return new SurfaceImage(size.Width, size.Height, pixels);
    }

    private SurfaceImage Image(string content, int width, int? height = null)
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
        int imageHeight = height ?? width;
        var pixels = new byte[width * imageHeight * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color;
            pixels[index + 3] = 255;
        }
        return new SurfaceImage(width, imageHeight, pixels);
    }
}
