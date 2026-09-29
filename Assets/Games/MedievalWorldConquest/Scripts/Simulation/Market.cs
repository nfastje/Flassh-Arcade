using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// An offer on the market, as in Tribal Wars: so much of one resource for so much of another, in a number of
    /// lots. The goods on offer (and the merchants to carry them) are set aside when it's posted, so it can always
    /// be delivered; whoever accepts a lot sends their side and gets the offer's side back.
    /// </summary>
    [Serializable]
    public class MarketOffer
    {
        public int Id;
        public int OwnerId, VillageId;
        public ResourceType Sell, Buy;
        /// <summary>Per lot.</summary>
        public int SellAmount, BuyAmount;
        /// <summary>Lots still open.</summary>
        public int Lots;
        public double Expires;

        public int MerchantsPerLot => World.MerchantsFor(SellAmount);
    }

    public enum TradeStatus
    {
        Ok,
        NoMarket,
        NothingToSend,
        SameVillage,
        InvalidTarget,
        NotEnoughResources,
        NotEnoughMerchants,
        /// <summary>An offer asking more than twice what it gives, or giving more than twice what it asks.</summary>
        UnfairRatio,
        OwnOffer,
        OfferGone,
    }

    /// <summary>
    /// The market: merchants (a market level buys more of them) carry up to 1,000 resources each to any village and
    /// come home empty; what arrives beyond the warehouse's room is lost, as in Tribal Wars. Offers let lords
    /// swap one resource for another.
    /// </summary>
    public partial class World
    {
        /// <summary>Game minutes a merchant takes to cross one map field.</summary>
        public const double MerchantMinutesPerField = 6;
        /// <summary>An offer can't ask more than this many times what it gives, nor give more than this many times what it asks.</summary>
        public const double MaxOfferRatio = 2;
        /// <summary>Game hours an offer stays up unless it's taken or withdrawn.</summary>
        public const double OfferHours = 24;

        public List<MarketOffer> Offers = new List<MarketOffer>();
        public int NextOfferId = 1;

        /// <summary>Merchants needed to carry an amount of resources.</summary>
        public static int MerchantsFor(int amount) => (amount + Buildings.MerchantCarry - 1) / Buildings.MerchantCarry;

        public static double MerchantSeconds(Village from, Village to) => Distance(from, to) * MerchantMinutesPerField * 60;

        /// <summary>Merchants the village's market keeps in all.</summary>
        public static int MerchantsTotal(Village v) => Buildings.Merchants(v.Level(BuildingType.Market));

        /// <summary>Merchants at home and free: not on the road, and not set aside for the village's offers.</summary>
        public int MerchantsFree(Village v)
        {
            int busy = 0;
            foreach (var c in CommandsOf(v.OwnerId))
                if ((c.Kind == CommandKind.Transport && c.FromVillageId == v.Id) || (c.Kind == CommandKind.TransportReturn && c.ToVillageId == v.Id))
                    busy += c.Merchants;
            foreach (var o in Offers)
                if (o.VillageId == v.Id) busy += o.Lots * o.MerchantsPerLot;
            return Math.Max(0, MerchantsTotal(v) - busy);
        }

        // ---------------------------------------------------------------- sending resources

        public TradeStatus CheckSendResources(Village from, Village to, Cost goods)
        {
            if (from == null || to == null) return TradeStatus.InvalidTarget;
            if (from == to) return TradeStatus.SameVillage;
            if (from.Level(BuildingType.Market) <= 0) return TradeStatus.NoMarket;
            int total = goods.Wood + goods.Clay + goods.Iron;
            if (goods.Wood < 0 || goods.Clay < 0 || goods.Iron < 0 || total <= 0) return TradeStatus.NothingToSend;
            Touch(from);
            if (!from.CanAfford(goods)) return TradeStatus.NotEnoughResources;
            if (MerchantsFor(total) > MerchantsFree(from)) return TradeStatus.NotEnoughMerchants;
            return TradeStatus.Ok;
        }

        /// <summary>Sends merchants with resources to another village. Returns the check's status.</summary>
        public TradeStatus SendResources(Village from, Village to, Cost goods)
        {
            var status = CheckSendResources(from, to, goods);
            if (status != TradeStatus.Ok) return status;
            from.Wood -= goods.Wood;
            from.Clay -= goods.Clay;
            from.Iron -= goods.Iron;
            Transport(from, to, goods);
            return TradeStatus.Ok;
        }

        /// <summary>Merchants set off (the goods already taken out of the sender's stores).</summary>
        Command Transport(Village from, Village to, Cost goods)
        {
            var command = March(CommandKind.Transport, from.OwnerId, from, to, new int[Units.Count], goods, MerchantSeconds(from, to));
            command.Merchants = MerchantsFor(goods.Wood + goods.Clay + goods.Iron);
            return command;
        }

        /// <summary>Merchants arrive: the goods go into storage (what won't fit is lost), and the merchants head home.</summary>
        void Deliver(Command command, Village from, Village to)
        {
            Touch(to);
            double cap = to.StorageCapacity;
            to.Wood = Math.Max(to.Wood, Math.Min(cap, to.Wood + command.Loot.Wood));
            to.Clay = Math.Max(to.Clay, Math.Min(cap, to.Clay + command.Loot.Clay));
            to.Iron = Math.Max(to.Iron, Math.Min(cap, to.Iron + command.Loot.Iron));

            if (IsHuman(to.OwnerId) || IsHuman(command.OwnerId))
                AddReport(new BattleReport
                {
                    Kind = ReportKind.ResourcesArrived,
                    AttackerVillageId = command.FromVillageId, DefenderVillageId = to.Id,
                    AttackerVillage = from?.Name ?? "?", DefenderVillage = to.Name,
                    AttackerX = from?.X ?? 0, AttackerY = from?.Y ?? 0, DefenderX = to.X, DefenderY = to.Y,
                    AttackerPlayer = from != null ? OwnerName(from) : "", DefenderPlayer = OwnerName(to),
                    AttackerPlayerId = command.OwnerId, DefenderPlayerId = to.OwnerId,
                    Loot = command.Loot,
                    // Read already if the player simply moved goods between their own villages.
                    Read = IsHuman(command.OwnerId) && IsHuman(to.OwnerId),
                });

            if (from != null)
            {
                var back = March(CommandKind.TransportReturn, command.OwnerId, to, from, new int[Units.Count], default, MerchantSeconds(to, from));
                back.Merchants = command.Merchants;
            }
        }

        // ---------------------------------------------------------------- offers

        static Cost Only(ResourceType r, int amount) =>
            r == ResourceType.Wood ? new Cost(amount, 0, 0) : r == ResourceType.Clay ? new Cost(0, amount, 0) : new Cost(0, 0, amount);

        public TradeStatus CheckOffer(Village v, ResourceType sell, int sellAmount, ResourceType buy, int buyAmount, int lots)
        {
            if (v == null) return TradeStatus.InvalidTarget;
            if (v.Level(BuildingType.Market) <= 0) return TradeStatus.NoMarket;
            if (sell == buy || sellAmount <= 0 || buyAmount <= 0 || lots <= 0) return TradeStatus.NothingToSend;
            if (buyAmount > sellAmount * MaxOfferRatio || sellAmount > buyAmount * MaxOfferRatio) return TradeStatus.UnfairRatio;
            Touch(v);
            if (v.Stock(sell) < (double)sellAmount * lots) return TradeStatus.NotEnoughResources;
            if (MerchantsFor(sellAmount) * lots > MerchantsFree(v)) return TradeStatus.NotEnoughMerchants;
            return TradeStatus.Ok;
        }

        /// <summary>Puts up an offer: the goods and merchants are set aside until it's taken, withdrawn, or runs out.</summary>
        public MarketOffer PostOffer(Village v, ResourceType sell, int sellAmount, ResourceType buy, int buyAmount, int lots)
        {
            if (CheckOffer(v, sell, sellAmount, buy, buyAmount, lots) != TradeStatus.Ok) return null;
            v.SetStock(sell, v.Stock(sell) - (double)sellAmount * lots);
            var offer = new MarketOffer
            {
                Id = NextOfferId++, OwnerId = v.OwnerId, VillageId = v.Id,
                Sell = sell, SellAmount = sellAmount, Buy = buy, BuyAmount = buyAmount, Lots = lots,
                Expires = Now + OfferHours * 3600,
            };
            Offers.Add(offer);
            Schedule(OfferHours * 3600, EventKind.OfferExpires, v.Id, offer.Id);
            return offer;
        }

        public MarketOffer FindOffer(int id) => Offers.Find(o => o.Id == id);

        /// <summary>Takes an offer down, returning what's left of its goods to its village.</summary>
        public bool WithdrawOffer(int offerId)
        {
            var offer = FindOffer(offerId);
            if (offer == null) return false;
            Offers.Remove(offer);
            var v = FindVillage(offer.VillageId);
            // A village that changed hands since keeps nothing: the goods went with the old owner's offer.
            if (v != null && v.OwnerId == offer.OwnerId)
            {
                Touch(v);
                v.SetStock(offer.Sell, v.Stock(offer.Sell) + (double)offer.SellAmount * offer.Lots);
            }
            return true;
        }

        void ExpireOffer(ScheduledEvent e) => WithdrawOffer(e.A);

        public TradeStatus CheckAccept(Village buyer, MarketOffer offer, int lots)
        {
            if (offer == null || !Offers.Contains(offer)) return TradeStatus.OfferGone;
            if (buyer == null) return TradeStatus.InvalidTarget;
            if (offer.OwnerId == buyer.OwnerId) return TradeStatus.OwnOffer;
            if (buyer.Level(BuildingType.Market) <= 0) return TradeStatus.NoMarket;
            if (lots <= 0 || lots > offer.Lots) return TradeStatus.NothingToSend;
            var seller = FindVillage(offer.VillageId);
            if (seller == null || seller.OwnerId != offer.OwnerId) return TradeStatus.OfferGone;
            Touch(buyer);
            if (buyer.Stock(offer.Buy) < (double)offer.BuyAmount * lots) return TradeStatus.NotEnoughResources;
            if (MerchantsFor(offer.BuyAmount) * lots > MerchantsFree(buyer)) return TradeStatus.NotEnoughMerchants;
            return TradeStatus.Ok;
        }

        /// <summary>
        /// Takes some lots of an offer: the buyer's merchants set off with the price, and the seller's with the goods
        /// that were set aside.
        /// </summary>
        public TradeStatus AcceptOffer(Village buyer, int offerId, int lots)
        {
            var offer = FindOffer(offerId);
            var status = CheckAccept(buyer, offer, lots);
            if (status != TradeStatus.Ok) return status;
            var seller = FindVillage(offer.VillageId);

            buyer.SetStock(offer.Buy, buyer.Stock(offer.Buy) - (double)offer.BuyAmount * lots);
            offer.Lots -= lots;
            if (offer.Lots == 0) Offers.Remove(offer);
            Transport(buyer, seller, Only(offer.Buy, offer.BuyAmount * lots));
            Transport(seller, buyer, Only(offer.Sell, offer.SellAmount * lots));
            return TradeStatus.Ok;
        }

        // ---------------------------------------------------------------- lords at the market

        /// <summary>How far (in fields) lords look for offers to take.</summary>
        public const double AiTradeRange = 30;

        /// <summary>
        /// A lord with a market evens out its stores: it takes fair offers nearby that swap what it has most of for
        /// what it has least of, and otherwise puts up an offer of its own.
        /// </summary>
        void AiTrade(Player lord, Village v)
        {
            if (v.Level(BuildingType.Market) <= 0) return;
            var (most, least) = MostAndLeast(v);
            double surplus = v.Stock(most) - v.Stock(least);
            if (surplus < 2000 || v.Stock(most) < 0.3 * v.StorageCapacity) return;

            // Someone else's offer that gives what we lack for what we have plenty of, at a fair price or better.
            for (int i = 0; i < Offers.Count && surplus >= 2000; i++)
            {
                var o = Offers[i];
                if (o.OwnerId == lord.Id || o.Sell != least || o.Buy != most || o.SellAmount < o.BuyAmount * 0.9) continue;
                var seller = FindVillage(o.VillageId);
                if (seller == null || Distance(v, seller) > AiTradeRange) continue;
                int lots = Math.Min(o.Lots, (int)(surplus / 2 / o.BuyAmount));
                lots = Math.Min(lots, MerchantsFree(v) / Math.Max(1, MerchantsFor(o.BuyAmount)));
                if (lots <= 0 || AcceptOffer(v, o.Id, lots) != TradeStatus.Ok) continue;
                surplus -= (double)(o.BuyAmount + o.SellAmount) * lots;
                i--; // the list may have shrunk
            }

            // Our own offer, one at a time per village: a thousand of the surplus for a thousand of the shortfall.
            if (surplus < 2000 || Offers.Exists(o => o.VillageId == v.Id)) return;
            int want = Math.Min(5, Math.Min(MerchantsFree(v), (int)(surplus / 2 / Buildings.MerchantCarry)));
            if (want > 0) PostOffer(v, most, Buildings.MerchantCarry, least, Buildings.MerchantCarry, want);
        }

        static (ResourceType most, ResourceType least) MostAndLeast(Village v)
        {
            ResourceType most = ResourceType.Wood, least = ResourceType.Wood;
            foreach (ResourceType r in ResourceTypes)
            {
                if (v.Stock(r) > v.Stock(most)) most = r;
                if (v.Stock(r) < v.Stock(least)) least = r;
            }
            return (most, least);
        }
    }
}
