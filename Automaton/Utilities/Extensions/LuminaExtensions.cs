using Lumina.Excel.Sheets;

namespace Automaton.Utilities.Extensions;
public static class LuminaExtensions
{
    public record IngredientExtension()
    {
        public required Item Item { get; init; }
        public required int Amount { get; init; }
    }

    public static IEnumerable<IngredientExtension> Ingredients(this Recipe recipe)
    {
        var output = new List<IngredientExtension>();
        for (var i = 0; i < recipe.Ingredient.Count; i++)
        {
            try
            {
                var item = recipe.Ingredient[i].Value;
                var amount = recipe.AmountIngredient[i];

                output.Add(new IngredientExtension() { Item = item, Amount = amount });
            }
            catch { }
        }

        return output;
    }

    public static string Print(this Item item) => $"[{item.RowId}] {item.Name}";
    public static string Print(this EventItem item) => $"[{item.RowId}] {item.Name}";
}
