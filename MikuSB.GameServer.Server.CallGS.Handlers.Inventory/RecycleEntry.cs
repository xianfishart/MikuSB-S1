using System.Collections.Generic;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

internal readonly record struct RecycleEntry(List<IReadOnlyList<uint>> FixedRewards, uint RecycleBase, decimal RecycleRatio);
