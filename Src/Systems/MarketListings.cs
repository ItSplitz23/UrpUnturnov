using SDG.Unturned;

namespace UrpUnturnov.Data
{
    public class MarketListing
    {
        public int Id { get; set; }
        public ushort ItemId { get; set; }
        public string ItemName { get; set; }
        public decimal Price { get; set; }
        public string SellerName { get; set; }
        public ulong SellerId { get; set; }
        public byte Quality { get; set; }
        public byte[] State { get; set; }
        public byte Quantity { get; set; }
        public System.DateTime ListedAt { get; set; }
    }
}