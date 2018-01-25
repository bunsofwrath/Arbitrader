﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Arbitrader.GW2API.Properties;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using Arbitrader.GW2API.Results;
using System.Collections.ObjectModel;
using Arbitrader.GW2API.Model;
using Arbitrader.GW2API.Entities;
using System.Diagnostics;

namespace Arbitrader.GW2API
{
    /// <summary>
    /// Contains descriptive data about items and recipes obtained from the GW2 API. Handles
    /// interactions with the SQL database adn allows replacement of or appending to existing
    /// data.
    /// </summary>
    public class ItemContext
    {
        #region Events
        /// <summary>
        /// Contains data for events raised at points throughout the data load process.
        /// </summary>
        public class DataLoadEventArgs : EventArgs
        {
            /// <summary>
            /// The GW2 API resource for which data is being loaded.
            /// </summary>
            public APIResource Resource { get; set; }

            /// <summary>
            /// The number of records affected since the last data load status update.
            /// </summary>
            public int? Count { get; set; }

            /// <summary>
            /// A message raised during the data load.
            /// </summary>
            public string Message { get; set; }

            /// <summary>
            /// Initializes a new instance of <see cref="DataLoadEventArgs"/>.
            /// </summary>
            /// <param name="resource">The GW2 API resource for which data is being loaded.</param>
            /// <param name="count">The number of records affected since the last data load status update.</param>
            /// <param name="message">A message raised during the data load.</param>
            public DataLoadEventArgs(APIResource resource, int? count = null, string message = null)
            {
                this.Resource = resource;
                this.Count = count;
                this.Message = message;
            }
        }

        /// <summary>
        /// Occurs when a data load has started.
        /// </summary>
        public event EventHandler<DataLoadEventArgs> DataLoadStarted;

        /// <summary>
        /// Occurs when a data load has finished.
        /// </summary>
        public event EventHandler<DataLoadEventArgs> DataLoadFinished;

        /// <summary>
        /// Occurs when a data load has a status update to report.
        /// </summary>
        public event EventHandler<DataLoadEventArgs> DataLoadStatusUpdate;

        /// <summary>
        /// Invokes any event handlers registered for <see cref="DataLoadStarted"/>.
        /// </summary>
        /// <param name="e">The arguments passed to the event handlers</param>
        protected virtual void OnDataLoadStarted(DataLoadEventArgs e)
        {
            DataLoadStarted?.Invoke(this, e);
        }

        /// <summary>
        /// Invokes any event handlers registered for <see cref="DataLoadFinished"/>.
        /// </summary>
        /// <param name="e">The arguments passed to the event handlers.</param>
        protected virtual void OnDataLoadFinished(DataLoadEventArgs e)
        {
            DataLoadFinished?.Invoke(this, e);
        }

        /// <summary>
        /// Invokes any event handlers registered for <see cref="DataLoadStatusUpdate"/>.
        /// </summary>
        /// <param name="e">The argumnets passed to the event handlers.</param>
        protected virtual void OnDataLoadStatusUpdate(DataLoadEventArgs e)
        {
            DataLoadStatusUpdate?.Invoke(this, e);
        }
        #endregion

        /// <summary>
        /// The number of records processed between occurences of <see cref="DataLoadStatusUpdate"/>.
        /// </summary>
        private int _updateInterval = 100;

        /// <summary>
        /// The maximum number of times a query to the GW2 API can return a failure result before the ID
        /// being queried will be skipped.
        /// </summary>
        private int _maxRetryCount = 5;

        /// <summary>
        /// Determines whether errors saving records to the database will terminate further processing or
        /// if such errors will be overlooked to allow processing to continue.
        /// </summary>
        private bool _continueOnError = true;

        /// <summary>
        /// True if the item/recipe mode has been built; false if not.
        /// </summary>
        private bool _isModelBuilt = false;

        /// <summary>
        /// The last HTTP client that was used to get results from the GW2 API.
        /// </summary>
        private HttpClient _httpClient;

        /// <summary>
        /// The set of recipes contained by the context.
        /// </summary>
        private List<Recipe> _recipes = new List<Recipe>();

        /// <summary>
        /// The set of items contained by the context.
        /// </summary>
        internal Items Items = new Items();

        internal List<Item> WatchedItems
        {
            get
            {
                var entities = new ArbitraderEntities();
                var watchedItemIDs = entities.WatchedItems.Select(i => i.APIID);
                return this.Items.Where(i => watchedItemIDs.Contains(i.ID)).ToList();
            }
        }

        /// <summary>
        /// Initializes a new instance of <see cref="ItemContext"/>.
        /// </summary>
        /// <param name="updateInterval">The number of records processed between occurences of <see cref="DataLoadStatusUpdate"/>.</param>
        /// <param name="continueOnError">Determines whether errors saving records to the database will terminate further processing or
        /// if such errors will be overlooked to allow processing to continue.</param>
        public ItemContext(int updateInterval = 100, bool continueOnError = true)
        {
            this._updateInterval = updateInterval;
            this._continueOnError = continueOnError;
        }

        /// <summary>
        /// Loads a resource from the GW2 API into the SQL database.
        /// </summary>
        /// <param name="client">The HTTP client used to get results from the GW2 API.</param>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="replace">Determines whether existing data in the database is overwritten or appended to.</param>
        public void Load(HttpClient client, APIResource resource, bool replace)
        {
            this._httpClient = client;
            this.InitializeHttpClient(client);
            this.Load(resource, replace);
        }

        public void Load(APIResource resource, bool replace)
        {
            using (var entities = new ArbitraderEntities())
            {
                this.LoadEntities(entities);

                if (replace)
                    this.DeleteExistingData(resource, entities);

                switch (resource)
                {
                    case APIResource.Items:
                        this._isModelBuilt = false;
                        this.UploadToDatabase<ItemResult, ItemEntity>(this._httpClient, resource, entities.Items, entities);
                        break;
                    case APIResource.Recipes:
                        this._isModelBuilt = false;
                        this.UploadToDatabase<RecipeResult, RecipeEntity>(this._httpClient, resource, entities.Recipes, entities);
                        break;
                    case APIResource.CommerceListings:
                        this.BuildModel(entities);
                        this.Items = this.Items.ExcludeNonSellable();
                        var ids = this.Items.Select(i => i.ID);
                        this.UploadToDatabase<ListingResult, ListingEntity>(this._httpClient, resource, entities.Listings, entities, ids);
                        this.Items.AttachListings(entities.Listings);
                        break;
                    default:
                        throw new ArgumentException($"Unable to load data for API resource \"{nameof(resource)}\".", nameof(resource));
                }
            }
        }

        /// <summary>
        /// Takes the data that has been loaded to the SQL database for items and recipes and constructs
        /// model objects to represent that data and its relationships.
        /// </summary>
        public void BuildModel(ArbitraderEntities entities, bool force = false)
        {
            if (this._isModelBuilt && !force)
                return;

            this._isModelBuilt = false;

            var watchedItemIDs = entities.WatchedItems.Select(i => i.APIID);

            //BUG: doesn't build the model for objects more than 1 level down the crafting tree from watched items
            IEnumerable<ItemEntity> watchedItems;
            IEnumerable<RecipeEntity> watchedRecipes;

            if (entities.WatchedItems.Any())
            {
                
                watchedItems = entities.Items.Where(i => watchedItemIDs.Contains(i.APIID));
                watchedRecipes = entities.Recipes.Where(r => r.OutputItemID.HasValue && watchedItemIDs.Contains(r.OutputItemID.Value));
            }
            else
            {
                watchedItems = entities.Items;
                watchedRecipes = entities.Recipes;
            }

            
            this._recipes = new List<Recipe>();

            foreach (var entity in watchedItems)
                if (!this.Items.Select(i => i.ID).Contains(entity.APIID))
                    this.Items.Add(new Item(entity));

            foreach (var entity in watchedRecipes)
                if (!this._recipes.Select(r => r.ID).Contains(entity.APIID))
                    this._recipes.Add(new Recipe(entity, this.GetItem));

            this._isModelBuilt = true;
        }

        /// <summary>
        /// Resolves a unique identifier in the GW2 API to an instance of <see cref="Item"/>.
        /// </summary>
        /// <param name="id">The unique identifier to be resolved.</param>
        /// <returns>An instance of <see cref="Item"/> with the specified ID.</returns>

        private Item GetItem(int id)
        {
            var existingItems = this.Items.Where(i => i.ID == id);

            if (existingItems.Any())
                return existingItems.First();

            var entities = new ArbitraderEntities();

            var entity = entities.Items.Where(i => i.APIID == id).First();
            this.Items.Add(new Item(entity));

            return existingItems.First();
        }

        internal void AddWatchedItem(Item item)
        {
            var entities = new ArbitraderEntities();

            if (!entities.Items.Any())
                throw new InvalidOperationException("Unable to add watched items before items have been loaded into the database.");

            var existingWatchedIDs = entities.WatchedItems.Select(i => i.APIID);

            if (!existingWatchedIDs.Contains(item.ID))
                entities.WatchedItems.Add(new WatchedItem(item.ID));

            entities.SaveChanges();
        }

        /// <summary>
        /// Adds all items whose names contain the given pattern to the list of watched items.
        /// </summary>
        /// <param name="pattern">The string pattern to search for.</param>
        public void AddWatchedItems(string pattern)
        {
            var entities = new ArbitraderEntities();

            if (!entities.Items.Any())
                throw new InvalidOperationException("Unable to add watched items before items have been loaded into the database.");

            var existingWatchedIDs = entities.WatchedItems.Select(i => i.APIID);
            var newWatchItems = entities.Items.Where(i => i.Name.ToUpper().Contains(pattern.ToUpper()))
                                              .Where(i => !existingWatchedIDs.Contains(i.ID));

            foreach (var item in newWatchItems)
                this.AddWatchedItem(new Item(item));

            entities.SaveChanges();
        }

        /// <summary>
        /// Clears the entire list of watched items.
        /// </summary>
        public void ClearWatchedItems()
        {
            var entities = new ArbitraderEntities();
            entities.WatchedItems.RemoveRange(entities.WatchedItems);
            entities.SaveChanges();
        }

        /// <summary>
        /// Removes items from the list of watched items by checking their names against the given string pattern.
        /// </summary>
        /// <param name="pattern">The string pattern to search for.</param>
        /// <param name="substring">If true, the pattern may match only a substring of the names of items
        /// to be removed. If false, it must match the entire name.</param>
        public void RemoveWatchedItem(string pattern, bool substring = true)
        {
            var entities = new ArbitraderEntities();

            if (!entities.Items.Any())
                throw new InvalidOperationException("Unable to remove watched items before items have been loaded into the database.");

            IEnumerable<int> matchingIDs;

            if (substring)
                matchingIDs = entities.Items.Where(i => i.Name.ToUpper().Contains(pattern.ToUpper()))
                                            .Select(i => i.APIID);
            else
                matchingIDs = entities.Items.Where(i => String.Compare(i.Name, pattern, true) == 0)
                                            .Select(i => i.APIID);

            var watchItemsToRemove = entities.WatchedItems.Where(i => matchingIDs.Contains(i.APIID));
            entities.WatchedItems.RemoveRange(watchItemsToRemove);
            entities.SaveChanges();
        }

        /// <summary>
        /// Loads each set of entities from the SQL database.
        /// </summary>
        /// <param name="entities">An interface for item, recipe, and market data stored in the Arbitrader SQL database.</param>
        private void LoadEntities(ArbitraderEntities entities)
        {
            entities.Items.Load();
            entities.ItemFlags.Load();

            entities.Disciplines.Load();
            entities.GuildIngredients.Load();
            entities.Ingredients.Load();
            entities.Recipes.Load();
            entities.RecipeFlags.Load();

            entities.WatchedItems.Load();
            entities.Listings.Load();
            entities.IndividualListings.Load();
        }

        public int GetCheapestPrice(string itemName, int count)
        {
            this.BuildModel(new ArbitraderEntities());

            var item = this.Items.Where(i => i.Name.Equals(itemName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

            if (item == null)
                throw new InvalidOperationException($"Could not find an item with name \"{itemName}\""); //TODO: use bespoke exception type

            return item.GetBestPrice(count);
        }

        /// <summary>
        /// Initializes the HTTP client that is used to send queries to and receive results from the GW2 API.
        /// </summary>
        /// <param name="client">The HTTP client used to interact with the GW2 API.</param>
        private void InitializeHttpClient(HttpClient client)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Deletes existing data from the SQL database. Respects foreign key relationships in that data.
        /// </summary>
        /// <param name="resource">The resource for which data is to be deleted.</param>
        /// <param name="entities">An interface for item, recipe, and market data stored in the Arbitrader SQL database.</param>
        private void DeleteExistingData(APIResource resource, ArbitraderEntities entities)
        {
            if (resource == APIResource.Recipes || resource == APIResource.Items)
            {
                entities.Disciplines.RemoveRange(entities.Disciplines);
                entities.RecipeFlags.RemoveRange(entities.RecipeFlags);
                entities.Ingredients.RemoveRange(entities.Ingredients);
                entities.GuildIngredients.RemoveRange(entities.GuildIngredients);
                entities.Recipes.RemoveRange(entities.Recipes);
            }

            if (resource == APIResource.Items)
            {
                entities.ItemFlags.RemoveRange(entities.ItemFlags);
                entities.Items.RemoveRange(entities.Items);
            }

            entities.SaveChanges();
        }

        /// <summary>
        /// Gets results from the GW2 API and saves those results to the SQL database.
        /// </summary>
        /// <typeparam name="R">The result type that query results from the GW2 API are to be filtered into.</typeparam>
        /// <typeparam name="E">The entity type that is to be used to save the result data to the SQL database.</typeparam>
        /// <param name="client">The HTTP client used to interact with the GW2 API.</param>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="targetDataSet">The dataset containing entities to be populated from results from the GW2 API.</param>
        /// <param name="entities">An interface for item, recipe, and market data stored in the Arbitrader SQL database.</param>
        private void UploadToDatabase<R, E>(HttpClient client, APIResource resource, DbSet<E> targetDataSet, ArbitraderEntities entities)
            where R : APIDataResult<E>
            where E : Entity
        {
            var ids = GetIds(client, resource, targetDataSet);
            this.UploadToDatabase<R, E>(client, resource, targetDataSet, entities, ids);
        }

        /// <summary>
        /// Gets results from the GW2 API and saves those results to the SQL database.
        /// </summary>
        /// <typeparam name="R">The result type that query results from the GW2 API are to be filtered into.</typeparam>
        /// <typeparam name="E">The entity type that is to be used to save the result data to the SQL database.</typeparam>
        /// <param name="client">The HTTP client used to interact with the GW2 API.</param>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="targetDataSet">The dataset containing entities to be populated from results from the GW2 API.</param>
        /// <param name="entities">An interface for item, recipe, and market data stored in the Arbitrader SQL database.</param>
        /// <param name="ids">The unique identifiers in the GW2 API for the items for which data is to be retrieved.</param>
        private void UploadToDatabase<R, E>(HttpClient client, APIResource resource, DbSet<E> targetDataSet, ArbitraderEntities entities, IEnumerable<int> ids)
            where R : APIDataResult<E>
            where E : Entity
        {
            if (ids == null)
                return;

            this.OnDataLoadStarted(new DataLoadEventArgs(resource, ids.Count()));

            var count = 0;
            E result = null;

            foreach (var id in ids)
            {
                count += 1;
                result = this.GetSingleResult<R, E>(client, resource, id) as E;

                if (result != null)
                    targetDataSet.Add(result);

                if (count % _updateInterval == 0)
                {
                    this.SaveChanges(resource, targetDataSet, entities, result);
                    this.OnDataLoadStatusUpdate(new DataLoadEventArgs(resource, count));
                }
            }

            this.SaveChanges(resource, targetDataSet, entities, result);
            this.OnDataLoadFinished(new DataLoadEventArgs(resource, null));
        }

        /// <summary>
        /// Returns a complete list of all possible IDs for the specified resource.
        /// </summary>
        /// <typeparam name="E">The entity type that is to be used to save the result data to the SQL database.</typeparam>
        /// <param name="client">The HTTP client used to interact with the GW2 API.</param>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="targetDataSet">The dataset containing entities to be populated from results from the GW2 API.</param>
        /// <returns>A complete list of all possible IDs for the specified resource.</returns>
        private static IEnumerable<int> GetIds<E>(HttpClient client, APIResource resource, DbSet<E> targetDataSet)
            where E : Entity
        {
            var baseURL = Settings.Default.APIBaseURL;
            var listURL = $"{baseURL}/{resource.GetPath()}";
            var response = client.GetAsync(listURL).Result;

            if (!response.IsSuccessStatusCode)
                return null;

            var queryableDbSet = (IQueryable<Entity>)targetDataSet;
            var existingIds = queryableDbSet.Select(row => row.APIID)
                                            .OrderBy(id => id)
                                            .ToList();
            var newIds = response.Content.ReadAsAsync<List<int>>().Result;            
            return newIds.Except(existingIds);
        }

        /// <summary>
        /// Saves a set of entity changes to the SQL database.
        /// </summary>
        /// <typeparam name="E">The entity type that is to be used to save the result data to the SQL database.</typeparam>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="targetDataSet">The dataset containing entities to be populated from results from the GW2 API.</param>
        /// <param name="entities">An interface for item, recipe, and market data stored in the Arbitrader SQL database.</param>
        /// <param name="result">The GW2 API query result containing data to be saved to the database.</param>
        private void SaveChanges<E>(APIResource resource, DbSet<E> targetDataSet, ArbitraderEntities entities, E result)
            where E : Entity
        {
            try
            {
                entities.SaveChanges();
            }
            catch (DbUpdateException e)
            {
                var message = $"Error saving changes for resource \"{resource}\". Exception message: {e.Message}";

                if (e.InnerException != null)
                    message += $" First sub-exception message: {e.InnerException.Message}";

                if (e.InnerException.InnerException != null)
                    message += $" Second sub-exception message: {e.InnerException.InnerException.Message}";

                message += $" : {e.StackTrace}";

                this.OnDataLoadStatusUpdate(new DataLoadEventArgs(resource, null, message));

                if (result != null)
                    targetDataSet.Remove(result);

                if (!this._continueOnError)
                    throw;
            }
        }

        /// <summary>
        /// Returns a result for a single ID from the GW2 API for the specified resource.
        /// </summary>
        /// <typeparam name="R">The result type that query results from the GW2 API are to be filtered into.</typeparam>
        /// <typeparam name="E">The entity type that is to be used to save the result data to the SQL database.</typeparam>
        /// <param name="client">The HTTP client used to interact with the GW2 API.</param>
        /// <param name="resource">The type of resource to get data for.</param>
        /// <param name="id">The ID of the result to be retrieved.</param>
        /// <returns>A result for a single ID from the GW2 API for the specified resource.</returns>
        private Entity GetSingleResult<R, E>(HttpClient client, APIResource resource, int id)
            where R : APIDataResult<E>
            where E : Entity
        {
            var baseURL = Settings.Default.APIBaseURL;
            var listURL = $"{baseURL}/{resource.GetPath()}";
            var singleResultURL = $"{listURL}/{id}";

            var retryCount = 0;

            while (retryCount < this._maxRetryCount)
            {
                var singleResultResponse = client.GetAsync(singleResultURL).Result;

                if (singleResultResponse.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Resource : {resource} | ID : {id} | Retry Count : {retryCount}");
                    return singleResultResponse.Content.ReadAsAsync<R>().Result.ToEntity();
                }

                if (singleResultResponse.ReasonPhrase == "Not Found")
                    return null;

                retryCount += 1;
            }

            return null;
        }
    }
}
