using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal class TakeScreenshotTool : MainThreadTool
    {
        public override string Name => "take_screenshot";
        public override string Description => "Capture the current game screen as a base64-encoded PNG image. Useful for AI to visually understand the current UI layout.";
        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["width"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Screenshot width (default: current screen width, max: 4096)"
                },
                ["height"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Screenshot height (default: current screen height, max: 4096)"
                }
            }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            int width = Screen.width;
            int height = Screen.height;

            if (args != null)
            {
                if (args.TryGetValue("width", out var wv))
                {
                    if (wv is double wd) width = (int)wd;
                    else if (wv is long wl) width = (int)wl;
                }
                if (args.TryGetValue("height", out var hv))
                {
                    if (hv is double hd) height = (int)hd;
                    else if (hv is long hl) height = (int)hl;
                }
            }

            width = Math.Max(16, Math.Min(4096, width));
            height = Math.Max(16, Math.Min(4096, height));

            var captured = ScreenCapture.CaptureScreenshotAsTexture();
            if (captured == null)
                return new McpContentItem { type = "text", text = "{\"error\":\"Failed to capture screenshot\"}", isError = true };

            Texture2D output = captured;
            if (captured.width != width || captured.height != height)
            {
                output = Resize(captured, width, height);
                if (output == null)
                {
                    UnityEngine.Object.DestroyImmediate(captured);
                    return new McpContentItem { type = "text", text = "{\"error\":\"Failed to resize screenshot\"}", isError = true };
                }
            }

            byte[] pngBytes = ImageConversion.EncodeToPNG(output);
            if (output != captured) UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(captured);

            if (pngBytes == null || pngBytes.Length == 0)
                return new McpContentItem { type = "text", text = "{\"error\":\"Failed to encode screenshot\"}", isError = true };

            string base64 = Convert.ToBase64String(pngBytes);
            return new McpContentItem
            {
                type = "image",
                data = base64,
                mimeType = "image/png"
            };
        }

        private static Texture2D Resize(Texture2D source, int width, int height)
        {
            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                var resized = new Texture2D(width, height, TextureFormat.RGB24, false);
                resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resized.Apply();
                return resized;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }
    }
}
