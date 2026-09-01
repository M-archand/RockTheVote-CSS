namespace cs2_rockthevote
{
    public class Map(string name, string? id)
    {
        public string? Id { get; set; } = id?.Trim();
        public string Name { get; set; } = name.Trim();
    }
}