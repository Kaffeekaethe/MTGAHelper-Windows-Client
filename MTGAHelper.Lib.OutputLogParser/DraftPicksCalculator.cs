﻿using System;
using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using MTGAHelper.Entity;
using Serilog;

namespace MTGAHelper.Lib.OutputLogParser
{
    public class DraftPicksCalculator
    {
        readonly IMapper mapper;
        readonly Dictionary<int, Card> allCards;
        //CacheSingleton<Dictionary<string, DraftRatings>> draftRatings;
        readonly Dictionary<string,
            Dictionary<string,
                (Dictionary<string, DraftRating> ratings, Dictionary<string, ICollection<DraftRatingTopCard>> topCardsByColor)>> ratingsBySourceSet;

        public DraftPicksCalculator(
            IMapper mapper,
            CacheSingleton<Dictionary<int, Card>> cacheAllCards,
            CacheSingleton<Dictionary<string, DraftRatings>> draftRatings)
        {
            this.mapper = mapper;
            this.allCards = cacheAllCards.Get();
            //this.draftRatings = draftRatings;
            var ratingsBySource = draftRatings.Get();
            ratingsBySourceSet = ratingsBySource.ToDictionary(
                source => source.Key, source => source.Value.RatingsBySet.ToDictionary(
                    set => set.Key, set => (
                        set.Value.Ratings.ToDictionary(card => card.CardName, card => card),
                        set.Value.TopCommonCardsByColor
                    )));
        }

        public DraftPicksCalculator Init(Dictionary<string, Dictionary<string, CustomDraftRating>> customRatingsBySetThenCardName)
        {
            var dictCustomRatings = customRatingsBySetThenCardName
                .ToDictionary(i => i.Key, i => (
                    i.Value.ToDictionary(x => x.Key, x => new DraftRating
                    {
                        CardName = x.Value.Name,
                        Description = x.Value.Note,
                        RatingValue = x.Value.Rating ?? 0f,
                        RatingToDisplay = x.Value.Rating?.ToString() ?? "N/A",
                    }),
                    (Dictionary<string, ICollection<DraftRatingTopCard>>)null
                ));

            ratingsBySourceSet["Your custom ratings"] = dictCustomRatings;

            return this;
        }

        public ICollection<CardForDraftPick> GetCardsForDraftPick(
            ICollection<int> cardPool,
            ICollection<int> pickedCards,
            string source,
            Dictionary<int, int> collection,
            ICollection<CardCompareInfo> raredraftingInfo
            )
        {
            if (cardPool.Any() == false)
                return new CardForDraftPick[0];

            var weights = raredraftingInfo.ToDictionary(i => i.GrpId, i => i);

            var byCard = cardPool
                .Where(i => allCards.ContainsKey(i))
                .Select(i => allCards[i]);

            var maxWeight = byCard.Max(i => weights.ContainsKey(i.grpId) ? weights[i.grpId].MissingWeight : 0f);

            var dataUnordered = byCard
                .Select(i =>
                {
                    var card = mapper.Map<CardForDraftPick>(i);

                    try
                    {
                        if (source == null)
                            Log.Error("GetCardsForDraftPick Source is null");
                        if (i.set == null)
                            Log.Error("GetCardsForDraftPick Card set is null {grpId}", i.grpId);
                        if (i.name == null)
                            Log.Error("GetCardsForDraftPick Card name is null", i.grpId);

                        card.NbMissingCollection = calculateMissingAmount(collection, card, pickedCards);

                        if (weights.ContainsKey(i.grpId))
                        {
                            card.NbMissingTrackedDecks = weights[i.grpId].NbMissing;
                            card.Weight = weights[i.grpId].MissingWeight;
                            card.NbDecksUsedMain = weights[i.grpId].NbDecksMain;
                            card.NbDecksUsedSideboard = weights[i.grpId].NbDecksSideboardOnly;
                        }

                        if (ratingsBySourceSet.ContainsKey(source) &&
                            ratingsBySourceSet[source].ContainsKey(i.set) &&
                            ratingsBySourceSet[source][i.set].ratings.ContainsKey(i.name))
                        {
                            var draftRating = ratingsBySourceSet[source][i.set].ratings[i.name];
                            card.Description = draftRating.Description;
                            card.RatingValue = draftRating.RatingValue;
                            card.RatingToDisplay = draftRating.RatingToDisplay;
                            card.RatingSource = source;

                            var cardColors = string.Join("", card.colors);
                            if (ratingsBySourceSet[source][i.set].topCardsByColor?.ContainsKey(cardColors) == true)
                            {
                                var rank = ratingsBySourceSet[source][i.set].topCardsByColor[card.colors.First()].FirstOrDefault(x => x.Name == card.name)?.Rank ?? 0;
                                card.TopCommonCard = new DraftRatingTopCard(rank, cardColors);
                            }
                        }
                        else
                        {
                            // Rating not found for card
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ERROR IN DRAFTPICK");
                        System.Diagnostics.Debugger.Break();
                    }

                    return card;
                });

            var data = dataUnordered
                .OrderByDescending(i => i.RatingValue)
                .ThenByDescending(i => i.Weight)
                .ThenByDescending(i => i.name)
                .ToArray();

            var isMissingRareLand = data.FirstOrDefault(i => i.type.Contains("Land") && i.GetRarityEnum() == RarityEnum.Rare && i.NbMissingCollection > 0);

            if (isMissingRareLand != null)
                isMissingRareLand.IsRareDraftPick = RaredraftPickReasonEnum.RareLandMissing;

            else if (maxWeight > 0 && data.Any(i => i.Weight == maxWeight))
                data.First(i => i.Weight == maxWeight).IsRareDraftPick = RaredraftPickReasonEnum.HighestWeight;

            else if (data.Any(i => i.type.StartsWith("Basic Land") == false && i.NbMissingCollection > 0))
            {
                var r = data
                    .Where(i => i.type.StartsWith("Basic Land") == false)
                    .Where(i => i.NbMissingCollection > 0)
                    .OrderBy(i => i.GetRarityEnum())
                    .ThenByDescending(i => i.NbMissingCollection)
                    .First();

                r.IsRareDraftPick = RaredraftPickReasonEnum.MissingInCollection;
            }
            else
            {
                var bestRarity = data.OrderBy(i => i.GetRarityEnum()).First().GetRarityEnum();
                var cardsMatching = data
                    .Where(i => i.GetRarityEnum() == bestRarity)
                    .Where(i => i.type.StartsWith("Basic Land") == false);

                foreach (var c in cardsMatching)
                    c.IsRareDraftPick = RaredraftPickReasonEnum.BestVaultRarity;
            }

            return data;
        }

        private int calculateMissingAmount(
            Dictionary<int, int> collection,
            Card card,
            ICollection<int> pickedCards)
        {
            // Check with collection
            var missingAmount = card.type.StartsWith("Basic Land") ? 0 :
                collection.ContainsKey(card.grpId) ? 4 - collection[card.grpId] : 4;

            // Consider picked cards during draft
            missingAmount -= pickedCards.Count(i => i == card.grpId);

            return Math.Max(0, missingAmount);
        }
    }
}
