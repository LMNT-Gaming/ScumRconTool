using System.Text.Json;
using ScumRconTool.Services;

if (args.Length != 1) throw new ArgumentException("Output path required.");
var items = new ItemCatalogService().Load()
    .Where(x => x.IsSpawnable)
    .Select(x => new
    {
        code = x.Code,
        name = x.DisplayName,
        category = x.Category,
        tier = x.Tier,
        imageUrl = "https://lmnt-gaming.net/images/items/" + Uri.EscapeDataString(x.Code + ".png")
    });
var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], json);
