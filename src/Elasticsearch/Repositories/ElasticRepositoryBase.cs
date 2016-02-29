﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Nest;
using Elasticsearch.Net;
using Foundatio.Caching;
using Foundatio.Elasticsearch.Extensions;
using Foundatio.Elasticsearch.Repositories.Queries.Options;
using Foundatio.Extensions;
using Foundatio.Logging;
using Foundatio.Messaging;
using Foundatio.Repositories;
using Foundatio.Repositories.Extensions;
using Foundatio.Repositories.Models;
using Foundatio.Utility;

namespace Foundatio.Elasticsearch.Repositories {
    public abstract class ElasticRepositoryBase<T> : ElasticReadOnlyRepositoryBase<T>, IRepository<T> where T : class, IIdentity, new() {
        protected internal readonly bool HasDates = typeof(IHaveDates).IsAssignableFrom(typeof(T));
        protected internal readonly bool HasCreatedDate = typeof(IHaveCreatedDate).IsAssignableFrom(typeof(T));

        protected ElasticRepositoryBase(ElasticRepositoryContext<T> context, ILoggerFactory loggerFactory = null) : base(context, loggerFactory) {
            NotificationsEnabled = Context.MessagePublisher != null;
        }
        
        public async Task<T> AddAsync(T document, bool addToCache = false, TimeSpan? expiresIn = null, bool sendNotification = true) {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            await AddAsync(new[] { document }, addToCache, expiresIn, sendNotification).AnyContext();
            return document;
        }

        public async Task AddAsync(ICollection<T> documents, bool addToCache = false, TimeSpan? expiresIn = null, bool sendNotification = true) {
            if (documents == null || documents.Count == 0)
                return;

            await OnDocumentsAddingAsync(documents).AnyContext();

            if (Context.Validator != null)
                foreach (var doc in documents)
                    await Context.Validator.ValidateAndThrowAsync(doc).AnyContext();

            var result = await Context.ElasticClient.IndexManyAsync(documents, GetParentIdFunc, GetDocumentIndexFunc).AnyContext();
            if (!result.IsValid)
                throw new ApplicationException(String.Join("\r\n", result.ItemsWithErrors.Select(i => i.Error)), result.ConnectionStatus.OriginalException);

            if (addToCache)
                await AddToCacheAsync(documents, expiresIn).AnyContext();

            await OnDocumentsAddedAsync(documents, sendNotification).AnyContext();
        }

        public async Task RemoveAsync(string id, bool sendNotification = true) {
            if (String.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));

            var document = await GetByIdAsync(id, true).AnyContext();
            if (document == null)
                return;

            await RemoveAsync(new[] { document }, sendNotification).AnyContext();
        }

        public Task RemoveAsync(T document, bool sendNotification = true) {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            return RemoveAsync(new[] { document }, sendNotification);
        }

        public async Task RemoveAsync(ICollection<T> documents, bool sendNotification = true) {
            if (documents == null || documents.Count == 0)
                return;

            await OnDocumentsRemovingAsync(documents).AnyContext();
            foreach (var g in documents.GroupBy(d => GetDocumentIndexFunc?.Invoke(d)))
                await Context.ElasticClient.DeleteByQueryAsync<T>(q => q.Query(q1 => q1.Ids(g.Select(d => d.Id))).Index(g.Key)).AnyContext();

            await OnDocumentsRemovedAsync(documents, sendNotification).AnyContext();
        }

        public async Task RemoveAllAsync() {
            if (IsCacheEnabled)
                await Cache.RemoveAllAsync().AnyContext();

            await RemoveAllAsync(new object(), false).AnyContext();
        }

        protected List<string> RemoveAllIncludedFields { get; } = new List<string>();

        protected async Task<long> RemoveAllAsync(object query, bool sendNotifications = true) {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            long recordsAffected = 0;

            var searchDescriptor = CreateSearchDescriptor(query)
                .Source(s => {
                    s.Include(f => f.Id);
                    if (RemoveAllIncludedFields.Count > 0)
                        s.Include(RemoveAllIncludedFields.ToArray());

                    return s;
                })
                .Size(Context.BulkBatchSize);

            var documents = (await Context.ElasticClient.SearchAsync<T>(searchDescriptor).AnyContext()).Documents.ToList();
            while (documents.Count > 0) {
                recordsAffected += documents.Count;
                await RemoveAsync(documents, sendNotifications).AnyContext();

                documents = (await Context.ElasticClient.SearchAsync<T>(searchDescriptor).AnyContext()).Documents.ToList();
            }

            return recordsAffected;
        }

        public async Task<T> SaveAsync(T document, bool addToCache = false, TimeSpan? expiresIn = null, bool sendNotifications = true) {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            await SaveAsync(new[] { document }, addToCache, expiresIn, sendNotifications).AnyContext();
            return document;
        }

        public async Task SaveAsync(ICollection<T> documents, bool addToCache = false, TimeSpan? expiresIn = null, bool sendNotifications = true) {
            if (documents == null || documents.Count == 0)
                return;

            if (documents.Any(d => String.IsNullOrEmpty(d.Id)))
                throw new ApplicationException("Id must be set when calling Save.");

            string[] ids = documents.Where(d => !String.IsNullOrEmpty(d.Id)).Select(d => d.Id).ToArray();
            var originalDocuments = ids.Length > 0 ? (await GetByIdsAsync(ids).AnyContext()).Documents : new List<T>();

            await OnDocumentsSavingAsync(documents, originalDocuments).AnyContext();

            if (Context.Validator != null)
                foreach (var doc in documents)
                    await Context.Validator.ValidateAndThrowAsync(doc).AnyContext();

            var result = await Context.ElasticClient.IndexManyAsync(documents, GetParentIdFunc, GetDocumentIndexFunc).AnyContext();
            if (!result.IsValid)
                throw new ApplicationException(String.Join("\r\n", result.ItemsWithErrors.Select(i => i.Error)), result.ConnectionStatus.OriginalException);

            if (addToCache)
                await AddToCacheAsync(documents, expiresIn).AnyContext();

            await OnDocumentsSavedAsync(documents, originalDocuments, sendNotifications).AnyContext();
        }
        
        protected async Task<long> UpdateAllAsync(object query, object update, bool sendNotifications = true) {
            long recordsAffected = 0;

            var searchDescriptor = CreateSearchDescriptor(query)
                .Source(s => s.Include(f => f.Id))
                .SearchType(SearchType.Scan)
                .Scroll("4s")
                .Size(Context.BulkBatchSize);

            var scanResults = await Context.ElasticClient.SearchAsync<T>(searchDescriptor).AnyContext();
           
            // Check to see if no scroll id was returned. This will occur when the index doesn't exist.
            if (!scanResults.IsValid || String.IsNullOrEmpty(scanResults.ScrollId))
                return 0;

            var results = await Context.ElasticClient.ScrollAsync<T>("4s", scanResults.ScrollId).AnyContext();
            while (results.Hits.Any()) {
                var bulkResult = await Context.ElasticClient.BulkAsync(b => {
                    string script = update as string;
                    if (script != null)
                        results.Hits.ForEach(h => b.Update<T>(u => u.Id(h.Id).Index(h.Index).Script(script)));
                    else
                        results.Hits.ForEach(h => b.Update<T, object>(u => u.Id(h.Id).Index(h.Index).Doc(update)));

                    return b;
                }).AnyContext();

                if (!bulkResult.IsValid) {
                    _logger.Error()
                        .Message("Error occurred while bulk updating")
                        .Exception(bulkResult.ConnectionStatus.OriginalException ?? bulkResult.RequestInformation.OriginalException)
                        .Property("ItemsWithErrors", bulkResult.ItemsWithErrors)
                        .Property("Error", bulkResult.ServerError)
                        .Property("Query", query)
                        .Property("Update", update)
                        .Write();
                    return 0;
                }

                if (IsCacheEnabled)
                    foreach (var d in results.Hits)
                        await Cache.RemoveAsync(d.Id).AnyContext();

                recordsAffected += results.Documents.Count();
                results = await Context.ElasticClient.ScrollAsync<T>("4s", results.ScrollId).AnyContext();
            }

            if (recordsAffected <= 0)
                return 0;

            if (sendNotifications)
                await SendNotificationsAsync(ChangeType.Saved).AnyContext();

            return recordsAffected;
        }

        #region Events

        public AsyncEvent<DocumentsEventArgs<T>> DocumentsAdding { get; } = new AsyncEvent<DocumentsEventArgs<T>>();

        private async Task OnDocumentsAddingAsync(ICollection<T> documents) {
            documents.EnsureIds(GetDocumentIdFunc);

            if (HasDates)
                documents.OfType<IHaveDates>().SetDates();
            else if (HasCreatedDate)
                documents.OfType<IHaveCreatedDate>().SetCreatedDates();

            if (DocumentsAdding != null)
                await DocumentsAdding.InvokeAsync(this, new DocumentsEventArgs<T>(documents, this)).AnyContext();

            await OnDocumentsChangingAsync(ChangeType.Added, documents).AnyContext();
        }

        public AsyncEvent<DocumentsEventArgs<T>> DocumentsAdded { get; } = new AsyncEvent<DocumentsEventArgs<T>>();

        private async Task OnDocumentsAddedAsync(ICollection<T> documents, bool sendNotifications) {
            if (DocumentsAdded != null)
                await DocumentsAdded.InvokeAsync(this, new DocumentsEventArgs<T>(documents, this)).AnyContext();

            var modifiedDocs = documents.Select(d => new ModifiedDocument<T>(d, null)).ToList();
            await OnDocumentsChangedAsync(ChangeType.Added, modifiedDocs).AnyContext();

            if (sendNotifications)
                await SendNotificationsAsync(ChangeType.Added, modifiedDocs).AnyContext();
        }

        public AsyncEvent<ModifiedDocumentsEventArgs<T>> DocumentsSaving { get; } = new AsyncEvent<ModifiedDocumentsEventArgs<T>>();

        private async Task OnDocumentsSavingAsync(ICollection<T> documents, ICollection<T> originalDocuments) {
            if (HasDates)
                documents.Cast<IHaveDates>().SetDates();

            var modifiedDocs = originalDocuments.FullOuterJoin(
                documents, cf => cf.Id, cf => cf.Id,
                (original, modified, id) => new { Id = id, Original = original, Modified = modified }).Select(m => new ModifiedDocument<T>( m.Modified, m.Original)).ToList();
            
            var savingDocs = modifiedDocs.Where(m => m.Original != null).ToList();
            if (savingDocs.Count > 0)
                await InvalidateCacheAsync(savingDocs).AnyContext();

            // if we couldn't find an original document, then it must be new.
            var addingDocs = modifiedDocs.Where(m => m.Original == null).Select(m => m.Value).ToList();
            if (addingDocs.Count > 0) {
                await InvalidateCacheAsync(addingDocs).AnyContext();
                await OnDocumentsAddingAsync(addingDocs).AnyContext();
            }

            if (savingDocs.Count == 0)
                return;

            if (DocumentsSaving != null)
                await DocumentsSaving.InvokeAsync(this, new ModifiedDocumentsEventArgs<T>(modifiedDocs, this)).AnyContext();

            await OnDocumentsChangingAsync(ChangeType.Saved, modifiedDocs).AnyContext();
        }

        public AsyncEvent<ModifiedDocumentsEventArgs<T>> DocumentsSaved { get; } = new AsyncEvent<ModifiedDocumentsEventArgs<T>>();

        private async Task OnDocumentsSavedAsync(ICollection<T> documents, ICollection<T> originalDocuments, bool sendNotifications) {
            var modifiedDocs = originalDocuments.FullOuterJoin(
                documents, cf => cf.Id, cf => cf.Id,
                (original, modified, id) => new { Id = id, Original = original, Modified = modified }).Select(m => new ModifiedDocument<T>(m.Modified, m.Original)).ToList();
            
            // if we couldn't find an original document, then it must be new.
            var addedDocs = modifiedDocs.Where(m => m.Original == null).Select(m => m.Value).ToList();
            if (addedDocs.Count > 0)
                await OnDocumentsAddedAsync(addedDocs, sendNotifications).AnyContext();

            var savedDocs = modifiedDocs.Where(m => m.Original != null).ToList();
            if (savedDocs.Count == 0)
                return;

            if (DocumentsSaved != null)
                await DocumentsSaved.InvokeAsync(this, new ModifiedDocumentsEventArgs<T>(modifiedDocs, this)).AnyContext();

            await OnDocumentsChangedAsync(ChangeType.Saved, savedDocs).AnyContext();

            if (sendNotifications)
                await SendNotificationsAsync(ChangeType.Saved, savedDocs).AnyContext();
        }

        public AsyncEvent<DocumentsEventArgs<T>> DocumentsRemoving { get; } = new AsyncEvent<DocumentsEventArgs<T>>();

        private async Task OnDocumentsRemovingAsync(ICollection<T> documents) {
            await InvalidateCacheAsync(documents).AnyContext();

            if (DocumentsRemoving != null)
                await DocumentsRemoving.InvokeAsync(this, new DocumentsEventArgs<T>(documents, this)).AnyContext();

            await OnDocumentsChangingAsync(ChangeType.Removed, documents).AnyContext();
        }

        public AsyncEvent<DocumentsEventArgs<T>> DocumentsRemoved { get; } = new AsyncEvent<DocumentsEventArgs<T>>();

        private async Task OnDocumentsRemovedAsync(ICollection<T> documents, bool sendNotifications) {
            if (DocumentsRemoved != null)
                await DocumentsRemoved.InvokeAsync(this, new DocumentsEventArgs<T>(documents, this)).AnyContext();

            await OnDocumentsChangedAsync(ChangeType.Removed, documents).AnyContext();

            if (sendNotifications)
                await SendNotificationsAsync(ChangeType.Removed, documents).AnyContext();
        }

        public AsyncEvent<DocumentsChangeEventArgs<T>> DocumentsChanging { get; } = new AsyncEvent<DocumentsChangeEventArgs<T>>();

        private Task OnDocumentsChangingAsync(ChangeType changeType, ICollection<T> documents) {
            return OnDocumentsChangingAsync(changeType, documents.Select(d => new ModifiedDocument<T>(d, null)).ToList());
        }

        private async Task OnDocumentsChangingAsync(ChangeType changeType, ICollection<ModifiedDocument<T>> documents) {
            if (DocumentsChanging == null)
                return;

            await DocumentsChanging.InvokeAsync(this, new DocumentsChangeEventArgs<T>(changeType, documents, this)).AnyContext();
        }

        public AsyncEvent<DocumentsChangeEventArgs<T>> DocumentsChanged { get; } = new AsyncEvent<DocumentsChangeEventArgs<T>>();

        private Task OnDocumentsChangedAsync(ChangeType changeType, ICollection<T> documents) {
            return OnDocumentsChangedAsync(changeType, documents.Select(d => new ModifiedDocument<T>(d, null)).ToList());
        }

        private async Task OnDocumentsChangedAsync(ChangeType changeType, ICollection<ModifiedDocument<T>> documents) {
            if (DocumentsChanged == null)
                return;

            await DocumentsChanged.InvokeAsync(this, new DocumentsChangeEventArgs<T>(changeType, documents, this)).AnyContext();
        }

        #endregion

        protected virtual async Task AddToCacheAsync(ICollection<T> documents, TimeSpan? expiresIn = null) {
            if (!IsCacheEnabled)
                return;

            foreach (var document in documents)
                await Cache.SetAsync(document.Id, document, expiresIn ?? TimeSpan.FromSeconds(RepositoryConstants.DEFAULT_CACHE_EXPIRATION_SECONDS)).AnyContext();
        }

        protected bool NotificationsEnabled { get; set; } = true;

        public bool BatchNotifications { get; set; }

        private Task SendNotificationsAsync(ChangeType changeType) {
            return SendNotificationsAsync(changeType, new List<T>());
        }

        private Task SendNotificationsAsync(ChangeType changeType, ICollection<T> documents) {
            return SendNotificationsAsync(changeType, documents.Select(d => new ModifiedDocument<T>(d, null)).ToList());
        }

        protected virtual async Task SendNotificationsAsync(ChangeType changeType, ICollection<ModifiedDocument<T>> documents) {
            if (!NotificationsEnabled)
                return;

            var delay = TimeSpan.FromSeconds(1.5);

            var options = Options as IQueryOptions;
            var supportsSoftDeletes = options != null && options.SupportsSoftDeletes;
            if (!documents.Any()) {
                await PublishChangeTypeMessageAsync(changeType, null, delay).AnyContext();
            } else if (BatchNotifications && documents.Count > 1) {
                // TODO: This needs to support batch notifications
                if (!supportsSoftDeletes || changeType != ChangeType.Saved) {
                    foreach (var doc in documents.Select(d => d.Value)) {
                        await PublishChangeTypeMessageAsync(changeType, doc, delay).AnyContext();
                    }

                    return;
                }
                var allDeleted = documents.All(d => d.Original != null && ((ISupportSoftDeletes)d.Original).IsDeleted == false && ((ISupportSoftDeletes)d.Value).IsDeleted);
                foreach (var doc in documents.Select(d => d.Value)) {
                    await PublishChangeTypeMessageAsync(allDeleted ? ChangeType.Removed : changeType, doc, delay).AnyContext();
                }
            } else {
                if (!supportsSoftDeletes) {
                    foreach (var d in documents)
                        await PublishChangeTypeMessageAsync(changeType, d.Value, delay).AnyContext();
                    return;
                }

                foreach (var d in documents) {
                    var docChangeType = changeType;
                    if (d.Original != null) {
                        var document = (ISupportSoftDeletes)d.Value;
                        var original = (ISupportSoftDeletes)d.Original;
                        if (original.IsDeleted == false && document.IsDeleted)
                            docChangeType = ChangeType.Removed;
                    }

                    await PublishChangeTypeMessageAsync(docChangeType, d.Value, delay).AnyContext();
                }
            }
        }

        protected Task PublishChangeTypeMessageAsync(ChangeType changeType, T document, TimeSpan delay) {
            return PublishChangeTypeMessageAsync(changeType, document, null, delay);
        }

        protected virtual Task PublishChangeTypeMessageAsync(ChangeType changeType, T document, IDictionary<string, object> data = null, TimeSpan? delay = null) {
            return PublishMessageAsync(new EntityChanged {
                ChangeType = changeType,
                Id = document?.Id,
                Type = EntityType,
                Data = new DataDictionary(data ?? new Dictionary<string, object>())
            }, delay);
        }
        
        protected async Task PublishMessageAsync<TMessageType>(TMessageType message, TimeSpan? delay = null) where TMessageType : class {
            if (Context.MessagePublisher == null)
                return;

            await Context.MessagePublisher.PublishAsync(message, delay).AnyContext();
        }
    }
}