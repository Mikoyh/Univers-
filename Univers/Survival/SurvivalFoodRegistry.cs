/*
 * =====================================================================
 *  SurvivalFoodRegistry.cs - Klasifikasi Makanan & Minuman Terraria 1.4.5
 *  Namespace: Univers.Survival (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using Terraria;
using Terraria.ID;

namespace Univers.Survival
{
    public readonly record struct FoodNutrition(
        double HungerGain,
        double SanityGain,
        double TemperatureDelta,
        string Label
    );

    public static class SurvivalFoodRegistry
    {
        public static bool TryGetFoodNutrition(int itemType, out FoodNutrition nutrition)
        {
            nutrition = default;
            if (itemType <= 0) return false;

            if (itemType == ItemID.LifeFruit)
            {
                nutrition = new FoodNutrition(50.0, 25.0, 0.0, "Life Fruit");
                return true;
            }

            if (itemType == ItemID.Honeyfin)
            {
                nutrition = new FoodNutrition(24.0, 10.0, -2.0, "Honeyfin");
                return true;
            }

            if (itemType == ItemID.BottledHoney)
            {
                nutrition = new FoodNutrition(15.0, 6.0, 0.0, "Bottled Honey");
                return true;
            }

            if (itemType == ItemID.BottledWater)
            {
                nutrition = new FoodNutrition(8.0, 4.0, -6.0, "Air Minum");
                return true;
            }

            if (itemType == ItemID.Mushroom)
            {
                nutrition = new FoodNutrition(10.0, 2.0, 0.0, "Jamur");
                return true;
            }

            if (itemType == ItemID.GlowingMushroom)
            {
                nutrition = new FoodNutrition(10.0, -3.0, 0.0, "Jamur Bercahaya");
                return true;
            }

            if (itemType == ItemID.CookedMarshmallow)
            {
                nutrition = new FoodNutrition(18.0, 9.0, 3.0, "Marshmallow Panggang");
                return true;
            }

            if (itemType == ItemID.CoffeeCup)
            {
                nutrition = new FoodNutrition(22.0, 16.0, 4.0, "Kopi Hangat");
                return true;
            }

            if (itemType == ItemID.Ale || itemType == ItemID.Sake)
            {
                nutrition = new FoodNutrition(12.0, 10.0, 2.0, "Minuman Fermentasi");
                return true;
            }

            if (itemType == ItemID.Eggnog)
            {
                nutrition = new FoodNutrition(20.0, 10.0, 3.0, "Eggnog");
                return true;
            }

            // Deteksi otomatis semua item makanan vanilla Terraria 1.4.x berdasarkan tier buff WellFed
            Item sample = new Item();
            sample.SetDefaults(itemType);

            if (sample.buffType == BuffID.WellFed3)
            {
                nutrition = new FoodNutrition(45.0, 12.0, 0.0, "Hidangan Mewah (Tier 3)");
                return true;
            }

            if (sample.buffType == BuffID.WellFed2)
            {
                nutrition = new FoodNutrition(30.0, 8.0, 0.0, "Makanan Lezat (Tier 2)");
                return true;
            }

            if (sample.buffType == BuffID.WellFed || (itemType < ItemID.Sets.IsFood.Length && ItemID.Sets.IsFood[itemType]))
            {
                bool isDrink = sample.useStyle == ItemUseStyleID.DrinkLiquid;
                double tempDelta = isDrink ? -5.0 : 0.0;
                double sanityBonus = isDrink ? 8.0 : 4.0;
                string label = isDrink ? "Minuman Segar" : "Makanan Ringan (Tier 1)";
                nutrition = new FoodNutrition(18.0, sanityBonus, tempDelta, label);
                return true;
            }

            return false;
        }
    }
}
