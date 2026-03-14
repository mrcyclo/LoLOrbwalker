namespace LoLOrbwalker
{
    public class ChampionStats
    {
        public double attackSpeed { get; set; } = 1;
    }

    public class ActivePlayer
    {
        public ChampionStats championStats { get; set; } = new();
    }
}
