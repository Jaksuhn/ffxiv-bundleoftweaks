using Dalamud.Interface;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

[Tweak(disabled: true)]
public class Tools : Tweak
{
    public override string Name => "Tools";
    public override string Description => "Various useful tools.";

    public override unsafe void DrawConfig()
    {
        if (ImGui.Button("Get recipes you can craft"))
        {
            var _recipes = GetSheet<Recipe>()
                .Where(r => r.ItemResult.Value.RowId != 0 && r.Ingredients().Any(x => x.Amount > 0 && x.Item.RowId != 0))
                .Where(r => r.Ingredients().All(x => InventoryManager.Instance()->GetInventoryItemCount(x.Item.RowId) >= x.Amount))
                .ToList();
            foreach (var recipe in _recipes)
            {
                ImGui.Icon(recipe.ItemResult.Value!.Icon, 25);
                ImGui.SameLine();
                ImGuiEx.TextV($"[{recipe.RowId}] {recipe.ItemResult.Value!.Name}");
                ImGui.SameLine();
                if (ImGui.IconButton(FontAwesomeIcon.BookOpen, $"{recipe.RowId}"))
                    AgentRecipeNote.Instance()->OpenRecipeByRecipeId(recipe.RowId);
            }
        }

        if (ImGui.Button("Get uncompleted recipes"))
        {
            var _recipes = GetSheet<Recipe>().Where(r => r.ItemResult.Value!.RowId != 0 && r.SecretRecipeBook.Value!.RowId == 0 && r.RecipeNotebookList.Value!.RowId == 0 && !QuestManager.IsRecipeComplete(r.RowId)).ToList();
            foreach (var recipe in _recipes)
            {
                ImGui.Icon(recipe.ItemResult.Value!.Icon, 25);
                ImGui.SameLine();
                ImGuiEx.TextV($"[{recipe.RowId}] {recipe.ItemResult.Value!.Name}");
                ImGui.SameLine();
                if (ImGui.IconButton(FontAwesomeIcon.BookOpen, $"{recipe.RowId}"))
                    AgentRecipeNote.Instance()->OpenRecipeByRecipeId(recipe.RowId);
            }
        }
    }
}
