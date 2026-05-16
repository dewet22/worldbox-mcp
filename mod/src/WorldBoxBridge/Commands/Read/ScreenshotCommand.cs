using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Captures the current game framebuffer and returns it as base64-encoded PNG.
/// </summary>
/// <remarks>
/// Uses Unity's <c>ScreenCapture.CaptureScreenshotAsTexture()</c> — main-thread only.
/// HttpBridge already marshals us onto the main thread because <c>RequiresMainThread=true</c>.
/// The texture is destroyed immediately after encoding to avoid VRAM/GC pressure.
/// </remarks>
internal sealed class ScreenshotCommand : ICommand
{
    public string Name => "screenshot";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Captures the current game framebuffer as a base64-encoded PNG. Useful so the agent "
        + "can see what it just did. Returns {format, width, height, base64}. The image is "
        + "the last completed frame, so any modification you just did is visible.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, CancellationToken cancellationToken)
    {
        Texture2D? tex = null;
        try
        {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            var width = tex.width;
            var height = tex.height;
            var png = tex.EncodeToPNG();
            var b64 = Convert.ToBase64String(png);
            return Task.FromResult<object?>(
                new
                {
                    format = "png",
                    width,
                    height,
                    base64 = b64,
                    bytes = png.Length,
                }
            );
        }
        finally
        {
            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
