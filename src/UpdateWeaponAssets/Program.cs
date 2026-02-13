using System.Text.Json;
using UMManager.Core.GamesService.JsonModels;
using UpdateWeaponAssets;

var client = new HttpClient();

client.DefaultRequestHeaders.Add("User-Agent", "UMManager");
client.DefaultRequestHeaders.Add("Accept", "application/json");

var response = await client.GetAsync("https://raw.githubusercontent.com/tokafew420/genshin-impact-tools/main/data/weapons.json");

var content = await response.Content.ReadAsStringAsync();

var root = JsonSerializer.Deserialize<JsonWeaponRoot>(content);

var weaponsJson = new List<JsonWeapon>();

foreach (var jsonWeaponRoot in root!.data)
{
    var jsonWeapon = new JsonWeapon()
    {
        DisplayName = jsonWeaponRoot.name,
        InternalName = jsonWeaponRoot.name,
        Rarity = jsonWeaponRoot.rarity,
        Image = jsonWeaponRoot.thumbnail,
        IsMultiMod = false,
        Type = jsonWeaponRoot.type
    };
    weaponsJson.Add(jsonWeapon);
}

var json = JsonSerializer.Serialize(weaponsJson, new JsonSerializerOptions()
{
    WriteIndented = true
});

await File.WriteAllTextAsync("weapons.json", json);

