using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityExplorerMcp.Core;

namespace UnityExplorerMcp.MCP.Tools
{
    internal sealed class SearchUiSpritesTool : MainThreadTool
    {
        public override string Name => "search_ui_sprites";
        public override string Description =>
            "Search loaded Sprite assets by name and show dimensions plus UI Image usage paths. Useful for reusing native game styles.";

        public override Dictionary<string, object> InputSchema => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["query"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Case-insensitive Sprite name fragment"
                },
                ["activeOnly"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Only count usage by active UI Images (default: false)"
                },
                ["maxResults"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum assets to return (default: 25, max: 100)"
                }
            },
            ["required"] = new List<string> { "query" }
        };

        public override object Execute(Dictionary<string, object> args) => null;

        public override McpContentItem ExecuteOnMain(Dictionary<string, object> args)
        {
            string query = ToolArgumentReader.GetString(args, "query") ?? "";
            bool activeOnly = ToolArgumentReader.GetBool(args, "activeOnly");
            int maxResults = Math.Max(1, Math.Min(100, ToolArgumentReader.GetInt(args, "maxResults", 25)));

            var usage = BuildUsage(activeOnly);
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            var results = new List<object>();
            int totalMatches = 0;
            var seen = new HashSet<int>();
            foreach (var sprite in sprites)
            {
                if (sprite == null || !seen.Add(sprite.GetInstanceID())) continue;
                if (sprite.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                totalMatches++;
                if (results.Count >= maxResults) continue;

                usage.TryGetValue(sprite.GetInstanceID(), out var spriteUsage);
                results.Add(new
                {
                    instanceId = sprite.GetInstanceID(),
                    name = sprite.name,
                    texture = sprite.texture != null ? sprite.texture.name : null,
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                    border = sprite.border.ToString(),
                    pivot = sprite.pivot.ToString(),
                    pixelsPerUnit = sprite.pixelsPerUnit,
                    usageCount = spriteUsage?.count ?? 0,
                    usagePaths = spriteUsage?.paths
                });
            }

            return new McpContentItem
            {
                type = "text",
                text = MCP.JsonHelper.Serialize(new
                {
                    totalMatches,
                    returned = results.Count,
                    truncated = totalMatches > results.Count,
                    sprites = results
                })
            };
        }

        private sealed class SpriteUsage
        {
            public int count;
            public readonly List<string> paths = new List<string>();
        }

        private static Dictionary<int, SpriteUsage> BuildUsage(bool activeOnly)
        {
            var usage = new Dictionary<int, SpriteUsage>();
            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null || image.sprite == null || image.gameObject.scene.name == null) continue;
                if (activeOnly && !image.gameObject.activeInHierarchy) continue;

                int id = image.sprite.GetInstanceID();
                if (!usage.TryGetValue(id, out var item))
                {
                    item = new SpriteUsage();
                    usage[id] = item;
                }
                item.count++;
                if (item.paths.Count < 5)
                    item.paths.Add(UiHierarchyTraverser.GetPath(image.transform));
            }
            return usage;
        }
    }
}
