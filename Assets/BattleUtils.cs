using UnityEngine;

public static class BattleUtils
{
    public struct DamageResult
    {
        public int Damage;
        public bool IsCritical;
    }

    public static DamageResult CalculateDamage(BattleCharacter attacker, BattleCharacter defender, Move move)
    {
        if (move.power == 0) return new DamageResult { Damage = 0, IsCritical = false };

        float baseDamage = (move.power * attacker.attack) / (defender.defense * defender.GetDefenseMultiplier());
        float typeEffectiveness = GetTypeEffectiveness(move.type, defender.type);
        float stab = move.type == attacker.type ? 1.05f : 1f;
        float critChance = move.name == "Headbutt" ? 0.15f : 0.1f;
        bool isCritical = Random.value < critChance;
        float critical = isCritical ? 1.5f : 1f;
        float totalModifier = typeEffectiveness * stab * critical;
        int randomNum = Random.Range(217, 256);
        float randomFactor = randomNum / 255f;
        int damage = Mathf.FloorToInt(baseDamage * totalModifier * randomFactor);
        return new DamageResult { Damage = damage, IsCritical = isCritical };
    }

    public static float GetTypeEffectiveness(ElementType attackType, ElementType defenderType)
    {
        if (attackType == defenderType) return 1f;
        switch (attackType)
        {
            case ElementType.Fire:
                if (defenderType == ElementType.Grass) return 1.05f;
                if (defenderType == ElementType.Water) return 0.8f;
                break;
            case ElementType.Water:
                if (defenderType == ElementType.Fire) return 1.05f;
                if (defenderType == ElementType.Grass) return 0.8f;
                break;
            case ElementType.Grass:
                if (defenderType == ElementType.Water) return 1.05f;
                if (defenderType == ElementType.Fire) return 0.8f;
                break;
        }
        return 1f;
    }

    public static string ApplyMoveEffect(BattleCharacter user, BattleCharacter target, Move move, ref int turnsLeft)
    {
        string effectLog = "";
        switch (move.name)
        {
            case "Intimidate":
                if (target.defenseDebuffPercentage < 65f)
                {
                    int oldDefense = target.defense;
                    target.intimidateCount++; // Increment the counter
                    float debuffAmount = 9f + target.intimidateCount; // normal ramp

                    // Special dynamic logic for Fire (Rageblaze)
                    if (target.type == ElementType.Fire)
                    {
                        float effectiveDebuffPct = (target.baseDefense - target.defense) / (float)target.baseDefense * 100f;
                        if (effectiveDebuffPct >= 65f)
                        {
                            debuffAmount = 1f;
                        }
                    }
                    else
                    {
                        // Normal characters: after 65% use only 1%
                        if (target.defenseDebuffPercentage >= 65f)
                        {
                            debuffAmount = 1f;
                        }
                    }

                    target.defenseDebuffPercentage += debuffAmount;
                    if (target.defenseDebuffPercentage > 65f)
                        target.defenseDebuffPercentage = 65f;

                    int newDefense = (int)(target.baseDefense * (1 - target.defenseDebuffPercentage / 100f));
                    target.ApplyDefenseChange(newDefense);
                    int loweredAmount = oldDefense - newDefense;
                    effectLog = $"{target.GetName()}'s defense lowered by {loweredAmount}";
                    target.TakeDamage(0, "Attacked", false);   // Ensure effect always triggers
                }
                else
                {
                    // Already at max debuff: still trigger a small effect so it's not an empty turn
                    target.TakeDamage(0, "Attacked", false);
                    effectLog = $"{target.GetName()}'s defense is already at maximum debuff";
                }
                break;
            case "Heal":
                int healAmount = (int)(user.maxHP * 0.25f);
                user.ApplyHeal(healAmount);
                effectLog = $"Healed {healAmount} HP";
                break;
        }
        return effectLog;
    }
}