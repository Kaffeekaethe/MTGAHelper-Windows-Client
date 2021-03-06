﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoMapper;
using MTGAHelper.Lib;
using MTGAHelper.Lib.OutputLogParser;
using MTGAHelper.Lib.OutputLogParser.InMatchTracking;
using MTGAHelper.Tracker.WPF.Models;
using MTGAHelper.Tracker.WPF.Tools;
using MTGAHelper.Tracker.WPF.Business;

namespace MTGAHelper.Tracker.WPF.ViewModels
{
    #region Enumeration

    public enum DisplayType
    {
        Percent,
        CountOnly,
        None
    }

    public enum CardsListOrder
    {
        ManaCost,
        DrawChance,
    }

    #endregion

    public class CardsListVM : BasicModel
    {
        readonly IMapper mapper;
        private readonly CardThumbnailDownloader CardThumbnailDownloader;
        #region Constructor

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="display"></param>
        /// <param name="cardsListOrder"></param>
        /// <param name="mapper"></param>
        public CardsListVM(DisplayType display, CardsListOrder cardsListOrder, IMapper mapper, CardThumbnailDownloader cardThumbnailDownloader)
        {
            CardsListOrder = cardsListOrder;
            Display = display;
            this.mapper = mapper;
            CardThumbnailDownloader = cardThumbnailDownloader;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Collection of cards
        /// </summary>
        public ObservableCollection<LibraryCardWithAmountVM> Cards { get; set; }

        /// <summary>
        /// Whether to show the card images
        /// </summary>
        public bool ShowImage
        {
            get => _ShowCardImage;
            set => SetField(ref _ShowCardImage, value, nameof(ShowImage));
        }

        /// <summary>
        /// Whether to show the draw percentage and card fraction
        /// </summary>
        public bool ShowDrawPercent => Display == DisplayType.Percent;

        /// <summary>
        /// Whether to show only the card count
        /// </summary>
        public bool ShowCardCountOnly => Display == DisplayType.CountOnly;

        /// <summary>
        /// Option for sorting the cards
        /// </summary>
        public CardsListOrder CardsListOrder { get; set; }

        /// <summary>
        /// String used for noting card pop-ups
        /// </summary>
        public string CardChosen { get; set; } = "TEST";

        /// <summary>
        /// Number of cards left in the deck
        /// </summary>
        public int CardCount => Stats.CardsLeftInDeck;

        /// <summary>
        /// Number of lands left in the deck
        /// </summary>
        public int LandCount => Stats.LandsLeftInDeck;

        /// <summary>
        /// Total number of lands in the original deck
        /// </summary>
        public int TotalLandsInitial => Stats.TotalLandsInitial;

        /// <summary>
        /// Percentage chance of drawing a land
        /// </summary>
        public float DrawLandPct => Stats.DrawLandPct;

        #endregion

        #region Private Backing Fields

        /// <summary>
        /// Whether to show the card images
        /// </summary>
        private bool _ShowCardImage = true;

        #endregion

        #region Private Fields

        /// <summary>
        /// What type of information to display
        /// </summary>
        private DisplayType Display { get; }

        /// <summary>
        /// Deck statistics
        /// </summary>
        private readonly Stats Stats = new Stats();

        /// <summary>
        /// Calculator for card border gradients
        /// </summary>
        private readonly BorderGradientCalculator GradientCalculator = new BorderGradientCalculator();

        #endregion

        #region Public Methods

        public void ResetCards()
        {
            Cards = null;
            Stats.Reset();
            OnPropertyChanged(string.Empty);
        }

        public void ConvertCardList(IReadOnlyCollection<CardDrawInfo> cardsRaw)
        {
            var cardsQuery = cardsRaw
                .Select(c => ConvertCard(c.GrpId, c.Amount, c.DrawChance));

            var cards = OrderByCardListOrder(cardsQuery).ToArray();

            if (Cards != null)
            {
                UpdateCards(cards);
                if (CardsListOrder == CardsListOrder.DrawChance)
                    Cards.Sort(c => c.OrderByDescending(i => i.DrawPercent));
                Stats.Refresh(Cards);
                NotifyStatsChanged();
                return;
            }

            if (cardsRaw.Any() == false)
                return;

            Cards = new ObservableCollection<LibraryCardWithAmountVM>(cards.Select(AddBorderColor).Select(c =>
            {
                // set IsAmountChanged = false
                c.Amount = c.Amount;
                return c;
            }));
            Stats.Reset(Cards);

            OnPropertyChanged(string.Empty);
        }

        #endregion

        #region Private Methods

        private IOrderedEnumerable<LibraryCardWithAmountVM> OrderByCardListOrder(IEnumerable<LibraryCardWithAmountVM> cardsQuery)
        {
            return CardsListOrder switch
            {
                CardsListOrder.ManaCost => cardsQuery.OrderBy(i => i.Type.Contains("Land") ? 1 : 0).ThenBy(i => i.Cmc),
                CardsListOrder.DrawChance => cardsQuery.OrderByDescending(i => i.DrawPercent).ThenBy(i => i.Type.Contains("Land") ? 1 : 0),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private bool ShouldComeBeforeByCardListOrder(LibraryCardWithAmountVM left, LibraryCardWithAmountVM right)
        {
            return CardsListOrder switch
            {
                CardsListOrder.ManaCost => left.Type.Contains("Land") || left.Cmc < right.Cmc,
                CardsListOrder.DrawChance => left.DrawPercent > right.DrawPercent,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private void UpdateCards(ICollection<LibraryCardWithAmountVM> newCards)
        {
            // Remove any card with 0 amount
            for (int i = Cards.Count - 1; i >= 0; i--)
            {
                if (Cards[i].Amount == 0)
                    Cards.RemoveAt(i);
            }

            var dictCards = newCards.ToDictionary(i => i.ArenaId, i => i);

            // Modify card counts for cards already in the collection
            for (int i = Cards.Count - 1; i >= 0; i--)
            {
                int grpId = Cards[i].ArenaId;

                if (!dictCards.TryGetValue(grpId, out var c))
                {
                    Cards[i].DrawPercent = 0f;
                    Cards[i].Amount = 0;
                    continue;
                }

                Cards[i].DrawPercent = c.DrawPercent;
                Cards[i].Amount = c.Amount;

                dictCards.Remove(grpId);
            }

            // exit early if done
            if (dictCards.Count <= 0)
                return;

            // Add cards that are not yet in the collection
            var cardsToAddUnordered = dictCards
                .Where(kvp => kvp.Value.Amount > 0)
                .Select(kvp => AddBorderColor(kvp.Value));
            var cardsToAdd = OrderByCardListOrder(cardsToAddUnordered).ToArray();

            var insertAt = 0;
            foreach (var cardToAdd in cardsToAdd)
            {
                // Find where to insert based on CMC
                while (insertAt < Cards.Count && ShouldComeBeforeByCardListOrder(Cards[insertAt], cardToAdd))
                    insertAt++;

                Cards.Insert(insertAt, cardToAdd);
                insertAt++;
            }
        }

        private LibraryCardWithAmountVM AddBorderColor(LibraryCardWithAmountVM card)
        {
            card.BorderGradient = GradientCalculator.CalculateBorderGradient(card);
            return card;
        }

        private LibraryCardWithAmountVM ConvertCard(int grpId, int amount, float drawChance)
        {
            var card = mapper.Map<Entity.Card>(grpId);

            var ret = new LibraryCardWithAmountVM
            {
                ArenaId = grpId,
                Amount = amount,
                Colors = card.colors,
                ColorIdentity = card.color_identity,
                ImageArtUrl = Utilities.GetThumbnailLocal(card.imageArtUrl),
                ImageCardUrl = card.imageCardUrl,
                Name = card.name,
                Rarity = card.rarity,
                DrawPercent = drawChance,
                Cmc = card.cmc,
                ManaCost = card.mana_cost,
                Type = card.type,
            };

            return ret;
        }

        private void NotifyStatsChanged()
        {
            OnPropertyChanged(nameof(CardCount));
            OnPropertyChanged(nameof(LandCount));
            OnPropertyChanged(nameof(DrawLandPct));
        }

        #endregion

        #region Internal Methods

        internal void SetCards(string cardChosen, ICollection<CardWpf> cards)
        {
            CardChosen = cardChosen;

            var data = cards.Select(i => ConvertCard(i.ArenaId, 1, 1f / cards.Count));
            Cards = new ObservableCollection<LibraryCardWithAmountVM>(data.Select(AddBorderColor));

            OnPropertyChanged(nameof(CardChosen));
            OnPropertyChanged(nameof(Cards));
        }

        #endregion
    }
}
