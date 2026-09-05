using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Captures the current game framebuffer, optionally downscales it, and returns it base64-encoded.
/// </summary>
/// <remarks>
/// Uses Unity's <c>ScreenCapture.CaptureScreenshotAsTexture()</c> — main-thread only.
/// HttpBridge already marshals us onto the main thread because <c>RequiresMainThread=true</c>.
/// A full Retina frame (3354×2654) is ~2.8 MB as PNG, ~3.8 MB once base64'd — far too much to
/// hand a language model per call — so by default the longest edge is clamped to
/// <see cref="ScreenshotScaler.DefaultMaxDimension"/> and the result is JPEG. Downscaling is a
/// GPU blit into a temporary RenderTexture (bilinear), read back into a small Texture2D.
/// All textures are destroyed immediately after encoding to avoid VRAM/GC pressure.
/// </remarks>
internal sealed class ScreenshotCommand : ICommand
{
    public string Name => "screenshot";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Captures the current game framebuffer as a base64-encoded image. Useful so the agent "
        + "can see what it just did. Downscaled so the longest edge is max_dimension pixels "
        + $"(default {ScreenshotScaler.DefaultMaxDimension}; 0 = full resolution) and encoded as "
        + "jpg (default, with quality 1-100) or png. Returns {format, width, height, "
        + "source_width, source_height, base64, bytes}. The image is the last completed frame.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "max_dimension",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 0),
                            new JProperty("default", ScreenshotScaler.DefaultMaxDimension),
                            new JProperty(
                                "description",
                                "Longest edge in pixels; the frame is shrunk to fit, never enlarged. 0 disables scaling."
                            )
                        )
                    ),
                    new JProperty(
                        "format",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "enum",
                                new JArray(ScreenshotScaler.Jpg, ScreenshotScaler.Png)
                            ),
                            new JProperty("default", ScreenshotScaler.Jpg)
                        )
                    ),
                    new JProperty(
                        "quality",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 1),
                            new JProperty("maximum", 100),
                            new JProperty("default", ScreenshotScaler.DefaultQuality),
                            new JProperty("description", "JPEG quality; ignored for png.")
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        // Screenshot leaks the entire map at once — gate it on ReadAll so FactionPlayers
        // under partial_intel can't bypass their fog-of-war by snapping a picture.
        ctx.Require(Permission.ReadAll);

        var maxDimension =
            args.Value<int?>("max_dimension") ?? ScreenshotScaler.DefaultMaxDimension;
        if (maxDimension < 0)
        {
            throw new BridgeRejectionException(
                ErrorCode.BadArgs,
                "max_dimension must be 0 (full resolution) or a positive pixel count."
            );
        }
        var quality = ScreenshotScaler.ClampQuality(
            args.Value<int?>("quality") ?? ScreenshotScaler.DefaultQuality
        );
        string format;
        try
        {
            format = ScreenshotScaler.NormalizeFormat(args.Value<string?>("format"));
        }
        catch (ArgumentException ex)
        {
            throw new BridgeRejectionException(ErrorCode.BadArgs, ex.Message);
        }

        Texture2D? source = null;
        Texture2D? scaled = null;
        try
        {
            source = ScreenCapture.CaptureScreenshotAsTexture();
            var target = ScreenshotScaler.Fit(source.width, source.height, maxDimension);
            var tex = source;
            if (target.IsScaled)
            {
                scaled = Downscale(source, target.Width, target.Height);
                tex = scaled;
            }
            var isPng = format == ScreenshotScaler.Png;
            var encoded = isPng ? tex.EncodeToPNG() : tex.EncodeToJPG(quality);
            return Task.FromResult<object?>(
                new
                {
                    format,
                    width = tex.width,
                    height = tex.height,
                    source_width = source.width,
                    source_height = source.height,
                    quality = isPng ? (int?)null : quality,
                    base64 = Convert.ToBase64String(encoded),
                    bytes = encoded.Length,
                }
            );
        }
        finally
        {
            if (scaled != null)
            {
                UnityEngine.Object.Destroy(scaled);
            }
            if (source != null)
            {
                UnityEngine.Object.Destroy(source);
            }
        }
    }

    private static Texture2D Downscale(Texture2D source, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var result = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            try
            {
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply(updateMipmaps: false);
            }
            catch
            {
                // ExecuteAsync's finally only destroys the texture we hand back, so a failed
                // GPU readback here would leak the native allocation on every attempt.
                UnityEngine.Object.Destroy(result);
                throw;
            }
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
