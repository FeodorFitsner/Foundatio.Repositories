﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Repositories.Elasticsearch.Tests.Models;
using Foundatio.Repositories.Models;
using Foundatio.Repositories.Utility;
using Foundatio.Utility;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Repositories.Elasticsearch.Tests {
    public sealed class ReadOnlyRepositoryTests : ElasticRepositoryTestBase {
        private readonly IdentityRepository _identityRepository;
        private readonly DailyLogEventRepository _dailyRepository;
        private readonly EmployeeRepository _employeeRepository;

        public ReadOnlyRepositoryTests(ITestOutputHelper output) : base(output) {
            _identityRepository = new IdentityRepository(_configuration);
            _dailyRepository = new DailyLogEventRepository(_configuration);
            _employeeRepository = new EmployeeRepository(_configuration);

            RemoveDataAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task CanCacheFindResult() {
            var employee = await _employeeRepository.AddAsync(EmployeeGenerator.Generate(age: 20));

            await _client.RefreshAsync();
            var employees = await _employeeRepository.GetAllByAgeAsync(20);
            Assert.Equal(1, employees.Documents.Count);

            var json = JsonConvert.SerializeObject(employees);
            var results = JsonConvert.DeserializeObject<FindResults<Employee>>(json);
            Assert.NotNull(results);
            Assert.Equal(1, results.Documents.Count);
        }

        [Fact]
        public async Task InvalidateCache() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Default, addToCache: true);
            Assert.NotNull(identity?.Id);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            await _identityRepository.InvalidateCacheAsync(identity);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            await _identityRepository.SaveAsync(identity, addToCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses); // Save will attempt to lookup the original document using the cache.

            await _identityRepository.InvalidateCacheAsync(new List<Identity> { identity });
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses);

            await _identityRepository.SaveAsync(new List<Identity> { identity }, addToCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(2, _cache.Misses); // Save will attempt to lookup the original document using the cache.

            await _identityRepository.InvalidateCacheAsync(new List<Identity> { identity });
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(2, _cache.Misses);

            await _identityRepository.SaveAsync(new List<Identity> { identity }, addToCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(3, _cache.Misses);

            await _identityRepository.InvalidateCacheAsync(identity);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(3, _cache.Misses);

            await _identityRepository.InvalidateCacheAsync(new List<Identity> { identity });
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(3, _cache.Misses);
        }

        [Fact]
        public async Task InvalidateCacheWithInvalidArguments() {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _identityRepository.InvalidateCacheAsync((Identity)null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _identityRepository.InvalidateCacheAsync((IReadOnlyCollection<Identity>)null));
            await _identityRepository.InvalidateCacheAsync(new List<Identity>());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _identityRepository.InvalidateCacheAsync(new List<Identity> { null }));
        }

        [Fact]
        public async Task Count() {
            Assert.Equal(0, await _identityRepository.CountAsync());

            var identity = IdentityGenerator.Default;
            var result = await _identityRepository.AddAsync(identity);
            Assert.Equal(identity, result);

            await _client.RefreshAsync();
            Assert.Equal(1, await _identityRepository.CountAsync());
        }

        [Fact]
        public async Task CountWithTimeSeries() {
            Assert.Equal(0, await _dailyRepository.CountAsync());

            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(createdUtc: SystemClock.UtcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            var nowLog = LogEventGenerator.Default;
            var result = await _dailyRepository.AddAsync(nowLog);
            Assert.Equal(nowLog, result);

            await _client.RefreshAsync();
            Assert.Equal(2, await _dailyRepository.CountAsync());
        }

        [Fact]
        public async Task GetById() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity?.Id);

            Assert.Equal(identity, await _identityRepository.GetByIdAsync(identity.Id));
        }

        [Fact]
        public async Task GetByIdWithCache() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity?.Id);

            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            Assert.Equal(identity, await _identityRepository.GetByIdAsync(identity.Id, useCache: true));
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses);

            Assert.Equal(identity, await _identityRepository.GetByIdAsync(identity.Id, useCache: true));
            Assert.Equal(1, _cache.Count);
            Assert.Equal(1, _cache.Hits);
            Assert.Equal(1, _cache.Misses);
        }

        [Fact]
        public async Task GetByIdAnyIdsWithCache() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity?.Id);

            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            Assert.Equal(identity, await _identityRepository.GetByIdAsync(identity.Id, useCache: true));
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses);

            string cacheKey = _cache.Keys.Single();
            var cacheValue = await _cache.GetAsync<Identity>(cacheKey);
            Assert.True(cacheValue.HasValue);
            Assert.Equal(identity, cacheValue.Value);

            var results = await _identityRepository.GetByIdsAsync(new[] { identity.Id }, useCache: true);
            Assert.Equal(1, results.Count);
            Assert.Equal(identity, results.First());
            Assert.Equal(1, _cache.Count);
            Assert.Equal(2, _cache.Hits);
            Assert.Equal(1, _cache.Misses);
            cacheValue = await _cache.GetAsync<Identity>(cacheKey);
            Assert.True(cacheValue.HasValue);
            Assert.Equal(identity, cacheValue.Value);

            await _identityRepository.InvalidateCacheAsync(identity);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(3, _cache.Hits);
            Assert.Equal(1, _cache.Misses);

            results = await _identityRepository.GetByIdsAsync(new[] { identity.Id }, useCache: true);
            Assert.Equal(1, results.Count);
            Assert.Equal(identity, results.First());
            Assert.Equal(1, _cache.Count);
            Assert.Equal(3, _cache.Hits);
            Assert.Equal(2, _cache.Misses);
            cacheValue = await _cache.GetAsync<Identity>(cacheKey);
            Assert.True(cacheValue.HasValue);
            Assert.Equal(identity, cacheValue.Value);

            Assert.Equal(identity, await _identityRepository.GetByIdAsync(identity.Id, useCache: true));
            Assert.Equal(1, _cache.Count);
            Assert.Equal(5, _cache.Hits);
            Assert.Equal(2, _cache.Misses);
            cacheValue = await _cache.GetAsync<Identity>(cacheKey);
            Assert.True(cacheValue.HasValue);
            Assert.Equal(identity, cacheValue.Value);
        }

        [Fact]
        public async Task GetByIdWithTimeSeries() {
            var utcNow = SystemClock.UtcNow;
            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(createdUtc: utcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            var nowLog = await _dailyRepository.AddAsync(LogEventGenerator.Default);
            Assert.NotNull(nowLog?.Id);

            Assert.Equal(yesterdayLog, await _dailyRepository.GetByIdAsync(yesterdayLog.Id));
            Assert.Equal(nowLog, await _dailyRepository.GetByIdAsync(nowLog.Id));
        }

        [Fact(Skip = "We need to look into how we want to handle this.")]
        public async Task GetByIdWithOutOfSyncIndex() {
            var utcNow = SystemClock.UtcNow;
            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(ObjectId.GenerateNewId().ToString(), createdUtc: utcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            Assert.Equal(yesterdayLog, await _dailyRepository.GetByIdAsync(yesterdayLog.Id));
        }

        [Fact]
        public async Task GetByIds() {
            var identity1 = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity1?.Id);

            var identity2 = await _identityRepository.AddAsync(IdentityGenerator.Generate());
            Assert.NotNull(identity2?.Id);

            var results = await _identityRepository.GetByIdsAsync(new[] { identity1.Id, identity2.Id });
            Assert.NotNull(results);
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task GetByIdsWithInvalidId() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Generate());
            Assert.NotNull(identity?.Id);

            var result = await _identityRepository.GetByIdsAsync(null);
            Assert.Equal(0, result.Count);

            result = await _identityRepository.GetByIdsAsync(new string[] { null });
            Assert.Equal(0, result.Count);

            result = await _identityRepository.GetByIdsAsync(new[] { IdentityGenerator.Default.Id, identity.Id });
            Assert.Equal(1, result.Count);
        }

        [Fact]
        public async Task GetByIdsWithCaching() {
            var identity1 = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity1?.Id);

            var identity2 = await _identityRepository.AddAsync(IdentityGenerator.Generate());
            Assert.NotNull(identity2?.Id);

            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            var results = await _identityRepository.GetByIdsAsync(new[] { identity1.Id }, useCache: true);
            Assert.NotNull(results);
            Assert.Equal(1, results.Count);
            Assert.Equal(identity1, results.First());
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses);

            results = await _identityRepository.GetByIdsAsync(new[] { identity1.Id, identity2.Id }, useCache: true);
            Assert.NotNull(results);
            Assert.Equal(2, results.Count);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(1, _cache.Hits);
            Assert.Equal(2, _cache.Misses);

            results = await _identityRepository.GetByIdsAsync(new[] { identity1.Id, identity2.Id }, useCache: true);
            Assert.NotNull(results);
            Assert.Equal(2, results.Count);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(3, _cache.Hits);
            Assert.Equal(2, _cache.Misses);

            var identity = await _identityRepository.GetByIdAsync(identity1.Id, useCache: true);
            Assert.Equal(identity1, identity);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(4, _cache.Hits);
            Assert.Equal(2, _cache.Misses);
        }

        [Fact]
        public async Task GetByIdsWithInvalidIdAndCaching() {
            var identity = await _identityRepository.AddAsync(IdentityGenerator.Generate());
            Assert.NotNull(identity?.Id);

            var result = await _identityRepository.GetByIdsAsync(null, useCache: true);
            Assert.Equal(0, result.Count);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            result = await _identityRepository.GetByIdsAsync(new string[] { null }, useCache: true);
            Assert.Equal(0, result.Count);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);

            result = await _identityRepository.GetByIdsAsync(new[] { IdentityGenerator.Default.Id, identity.Id }, useCache: true);
            Assert.Equal(1, result.Count);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(2, _cache.Misses);
        }

        [Fact]
        public async Task GetByIdsWithTimeSeries() {
            var utcNow = SystemClock.UtcNow;
            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(createdUtc: utcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            var nowLog = await _dailyRepository.AddAsync(LogEventGenerator.Default);
            Assert.NotNull(nowLog?.Id);

            var results = await _dailyRepository.GetByIdsAsync(new[] { yesterdayLog.Id, nowLog.Id });
            Assert.NotNull(results);
            Assert.Equal(2, results.Count);
        }

        [Fact(Skip = "We need to look into how we want to handle this.")]
        public async Task GetByIdsWithOutOfSyncIndex() {
            var utcNow = SystemClock.UtcNow;
            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(ObjectId.GenerateNewId().ToString(), createdUtc: utcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            var results = await _dailyRepository.GetByIdsAsync(new[] { yesterdayLog.Id });
            Assert.NotNull(results);
            Assert.Equal(1, results.Count);
        }

        [Fact]
        public async Task GetAll() {
            var identities = IdentityGenerator.GenerateIdentities(25);
            await _identityRepository.AddAsync(identities);

            await _client.RefreshAsync();
            var results = await _identityRepository.GetAllAsync(paging: 100);
            Assert.NotNull(results);
            Assert.Equal(25, results.Total);
            Assert.Equal(25, results.Documents.Count);
            Assert.Equal(identities.OrderBy(i => i.Id), results.Documents.OrderBy(i => i.Id));
        }

        [Fact]
        public async Task GetAllWithPaging() {
            var identity1 = await _identityRepository.AddAsync(IdentityGenerator.Default);
            Assert.NotNull(identity1?.Id);

            var identity2 = await _identityRepository.AddAsync(IdentityGenerator.Generate());
            Assert.NotNull(identity2?.Id);

            await _client.RefreshAsync();
            var results = await _identityRepository.GetAllAsync(paging: new PagingOptions().WithLimit(1));
            Assert.NotNull(results);
            Assert.Equal(1, results.Documents.Count);
            Assert.Equal(1, results.Page);
            Assert.True(results.HasMore);
            Assert.Equal(2, results.Total);

            Assert.True(await results.NextPageAsync());
            Assert.Equal(1, results.Documents.Count);
            Assert.Equal(2, results.Page);
            Assert.Equal(2, results.Total);
            Assert.False(results.HasMore);
            var secondDoc = results.Documents.First();

            Assert.False(await results.NextPageAsync());
            Assert.Equal(0, results.Documents.Count);
            Assert.Equal(2, results.Page);
            Assert.False(results.HasMore);
            Assert.Equal(2, results.Total);

            var secondPageResults = await _identityRepository.GetAllAsync(paging: new PagingOptions().WithPage(2).WithLimit(1));
            Assert.Equal(secondDoc, secondPageResults.Documents.First());

            //TODO Ensure we are using snapshot paging...
        }

        [Fact]
        public async Task Exists() {
            Assert.False(await _identityRepository.ExistsAsync(null));

            var identity = IdentityGenerator.Default;
            Assert.False(await _identityRepository.ExistsAsync(identity.Id));

            var result = await _identityRepository.AddAsync(identity);
            Assert.Equal(identity, result);

            await _client.RefreshAsync();
            Assert.True(await _identityRepository.ExistsAsync(identity.Id));
        }

        [Fact]
        public async Task ExistsWithTimeSeries() {
            Assert.False(await _dailyRepository.ExistsAsync(null));

            var utcNow = SystemClock.UtcNow;
            var yesterdayLog = await _dailyRepository.AddAsync(LogEventGenerator.Generate(createdUtc: utcNow.AddDays(-1)));
            Assert.NotNull(yesterdayLog?.Id);

            var nowLog = await _dailyRepository.AddAsync(LogEventGenerator.Default);
            Assert.NotNull(nowLog?.Id);

            await _client.RefreshAsync();
            Assert.True(await _dailyRepository.ExistsAsync(yesterdayLog.Id));
            Assert.True(await _dailyRepository.ExistsAsync(nowLog.Id));
        }

        [Fact]
        public async Task ShouldNotIncludeWhenDeleted() {
            var deletedEmployee = EmployeeGenerator.Generate(age: 20, name: "Deleted");
            deletedEmployee.IsDeleted = true;
            await _employeeRepository.AddAsync(deletedEmployee);

            var employee2 = await _employeeRepository.AddAsync(EmployeeGenerator.Generate(age: 20));
            await _client.RefreshAsync();

            var employees = await _employeeRepository.GetAllByAgeAsync(20);

            Assert.Equal(1, employees.Documents.Count);
        }

        [Fact]
        public async Task SearchShouldNotReturnDeletedDocuments() {
            var employee = EmployeeGenerator.Generate(age: 20, name: "Deleted");
            employee = await _employeeRepository.AddAsync(employee);

            await _client.RefreshAsync();

            employee.IsDeleted = true;
            await _employeeRepository.SaveAsync(employee);

            var employees = await _employeeRepository.GetAllByAgeAsync(20);
            var employeeById = await _employeeRepository.GetByIdAsync(employee.Id);

            Assert.NotNull(employeeById);
            Assert.True(employeeById.IsDeleted);

            Assert.Equal(0, employees.Total);
        }
    }
}