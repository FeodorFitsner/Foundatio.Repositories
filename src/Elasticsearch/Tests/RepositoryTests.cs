﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Caching;
using Foundatio.Jobs;
using Foundatio.Logging.Xunit;
using Foundatio.Queues;
using Foundatio.Repositories.Elasticsearch.Tests.Configuration;
using Foundatio.Repositories.Elasticsearch.Tests.Extensions;
using Foundatio.Repositories.Elasticsearch.Tests.Models;
using Foundatio.Repositories.Utility;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Repositories.Elasticsearch.Tests {
    public class RepositoryTests : TestWithLoggingBase {
        private readonly InMemoryCacheClient _cache = new InMemoryCacheClient();
        private readonly IQueue<WorkItemData> _workItemQueue = new InMemoryQueue<WorkItemData>();
        private readonly MyAppDatabase _database;
        private readonly EmployeeRepository _repository;

        public RepositoryTests(ITestOutputHelper output): base(output) {
            Log.MinimumLevel = Logging.LogLevel.Trace;

            var connectionString = ConfigurationManager.ConnectionStrings["ElasticConnectionString"].ConnectionString;
            _database = new MyAppDatabase(new Uri(connectionString), _workItemQueue, _cache);
            _repository = new EmployeeRepository(new RepositoryConfiguration<Employee>(_database.Client, _database.Employees.Employee, cache: _cache), Log);
        }
        
        //[Fact]
        //public async Task GetByDateBasedIndex() {
        //    await RemoveDataAsync();

        //    var indexes = await _client.GetIndicesPointingToAliasAsync(_monthlyEmployeeIndex.AliasName);
        //    Assert.Equal(0, indexes.Count);
            
        //    var alias = await _client.GetAliasAsync(descriptor => descriptor.Alias(_monthlyEmployeeIndex.AliasName));
        //    Assert.False(alias.IsValid);
        //    Assert.Equal(0, alias.Indices.Count);

        //    var employee = await _monthlyRepository.AddAsync(EmployeeGenerator.Default);
        //    Assert.NotNull(employee?.Id);
            
        //    employee = await _monthlyRepository.AddAsync(EmployeeGenerator.Generate(startDate: DateTimeOffset.Now.SubtractMonths(1)));
        //    Assert.NotNull(employee?.Id);

        //    await _client.RefreshAsync();
        //    alias = await _client.GetAliasAsync(descriptor => descriptor.Alias(_monthlyEmployeeIndex.AliasName));
        //    Assert.True(alias.IsValid);
        //    Assert.Equal(2, alias.Indices.Count);
            
        //    indexes = await _client.GetIndicesPointingToAliasAsync(_monthlyEmployeeIndex.AliasName);
        //    Assert.Equal(2, indexes.Count);
        //}

        [Fact]
        public async Task AddWithDefaultGeneratedIdAsync() {
            await RemoveDataAsync();

            var employee = await _repository.AddAsync(EmployeeGenerator.Default);
            Assert.NotNull(employee?.Id);
            Assert.Equal(EmployeeGenerator.Default.Name, employee.Name);
            Assert.Equal(EmployeeGenerator.Default.Age, employee.Age);
            Assert.Equal(EmployeeGenerator.Default.CompanyName, employee.CompanyName);
            Assert.Equal(EmployeeGenerator.Default.CompanyId, employee.CompanyId);
        }

        [Fact]
        public async Task AddWithExistingIdAsync() {
            await RemoveDataAsync();

            string id = ObjectId.GenerateNewId().ToString();
            var employee = await _repository.AddAsync(EmployeeGenerator.Generate(id));
            Assert.Equal(id, employee?.Id);
        }

        [Fact]
        public async Task SaveAsync() {
            await RemoveDataAsync();

            var employee = await _repository.AddAsync(EmployeeGenerator.Default);
            Assert.NotNull(employee?.Id);
            Assert.Equal(EmployeeGenerator.Default.Name, employee.Name);

            employee.Name = Guid.NewGuid().ToString();

            var result = await _repository.SaveAsync(employee);
            Assert.Equal(employee.Name, result?.Name);
        }

        [Fact]
        public async Task AddDuplicateAsync() {
            await RemoveDataAsync();

            string id = ObjectId.GenerateNewId().ToString();
            var employee = await _repository.AddAsync(EmployeeGenerator.Generate(id));
            Assert.Equal(id, employee?.Id);

            employee = await _repository.AddAsync(EmployeeGenerator.Generate(id));
            Assert.Equal(id, employee?.Id);
        }

        [Fact]
        public async Task SetCreatedAndModifiedTimesAsync() {
            await RemoveDataAsync();

            DateTime nowUtc = DateTime.UtcNow;
            var employee = await _repository.AddAsync(EmployeeGenerator.Default);
            Assert.True(employee.CreatedUtc >= nowUtc);
            Assert.True(employee.UpdatedUtc >= nowUtc);

            DateTime createdUtc = employee.CreatedUtc;
            DateTime updatedUtc = employee.UpdatedUtc;

            employee.Name = Guid.NewGuid().ToString();
            await Task.Delay(100);
            employee = await _repository.SaveAsync(employee);
            Assert.Equal(createdUtc, employee.CreatedUtc);
            Assert.True(updatedUtc < employee.UpdatedUtc, $"Previous UpdatedUtc: {updatedUtc} Current UpdatedUtc: {employee.UpdatedUtc}");
        }

        [Fact]
        public async Task CannotSetFutureCreatedAndModifiedTimesAsync() {
            await RemoveDataAsync();

            var employee = await _repository.AddAsync(EmployeeGenerator.Generate(createdUtc: DateTime.MaxValue, updatedUtc: DateTime.MaxValue));
            Assert.True(employee.CreatedUtc != DateTime.MaxValue);
            Assert.True(employee.UpdatedUtc != DateTime.MaxValue);
            
            employee.CreatedUtc = DateTime.MaxValue;
            employee.UpdatedUtc = DateTime.MaxValue;

            employee = await _repository.SaveAsync(employee);
            Assert.True(employee.CreatedUtc != DateTime.MaxValue);
            Assert.True(employee.UpdatedUtc != DateTime.MaxValue);
        }

        [Fact]
        public async Task CanGetByIds() {
            var employee = await _repository.AddAsync(EmployeeGenerator.Generate());
            Assert.NotNull(employee.Id);

            var result = await _repository.GetByIdAsync(employee.Id);
            Assert.NotNull(result);
            Assert.Equal(employee.Id, result.Id);
            
            var employee2 = await _repository.AddAsync(EmployeeGenerator.Generate());
            Assert.NotNull(employee2.Id);

            var results = await _repository.GetByIdsAsync(new [] { employee.Id, employee2.Id });
            Assert.NotNull(results);
            Assert.Equal(2, results.Total);
        }

        [Fact]
        public async Task CanAddToCacheAsync() {
            await RemoveDataAsync();

            Assert.Equal(0, _cache.Count);
            var employee = await _repository.AddAsync(EmployeeGenerator.Default, addToCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);

            var cachedResult = await _repository.GetByIdAsync(employee.Id, useCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(1, _cache.Hits);
            Assert.Equal(employee.ToJson(), cachedResult.ToJson());
        }

        [Fact]
        public async Task CanSaveToCacheAsync() {
            await RemoveDataAsync();

            Assert.Equal(0, _cache.Count);
            var employee = await _repository.SaveAsync(EmployeeGenerator.Generate(ObjectId.GenerateNewId().ToString()), addToCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);

            var cachedResult = await _repository.GetByIdAsync(employee.Id, useCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(1, _cache.Hits);
            Assert.Equal(employee.ToJson(), cachedResult.ToJson());
        }
        
        [Fact]
        public async Task GetFromCacheAsync() {
            await RemoveDataAsync();

            var employees = new List<Employee> { EmployeeGenerator.Default, EmployeeGenerator.Generate() };

            Assert.Equal(0, _cache.Count);
            await _repository.AddAsync(employees);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            
            var cachedResult = await _repository.GetByIdsAsync(employees.Select(e => e.Id).ToArray(), useCache: true);
            Assert.NotNull(cachedResult);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(0, _cache.Hits);

            cachedResult = await _repository.GetByIdsAsync(employees.Select(e => e.Id).ToArray(), useCache: true);
            Assert.NotNull(cachedResult);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(2, _cache.Hits);

            await _repository.GetByIdAsync(employees.First().Id, useCache: true);
            Assert.Equal(2, _cache.Count);
            Assert.Equal(3, _cache.Hits);
        }

        [Fact]
        public async Task GetByIdsFromCacheAsync() {
            await RemoveDataAsync();

            Assert.Equal(0, _cache.Count);
            var employee = await _repository.AddAsync(EmployeeGenerator.Default);
            Assert.Equal(0, _cache.Count);
            Assert.Equal(0, _cache.Hits);

            var cachedResult = await _repository.GetByIdAsync(employee.Id, useCache: true);
            Assert.NotNull(cachedResult);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(employee.ToJson(), cachedResult.ToJson());

            cachedResult = await _repository.GetByIdAsync(employee.Id, useCache: true);
            Assert.Equal(1, _cache.Count);
            Assert.Equal(1, _cache.Hits);
            Assert.Equal(employee.ToJson(), cachedResult.ToJson());
        }

        [Fact]
        public async Task GetByAgeAsync() {
            await RemoveDataAsync();
            
            var employee19 = await _repository.AddAsync(EmployeeGenerator.Generate(age: 19));
            var employee20 = await _repository.AddAsync(EmployeeGenerator.Generate(age: 20));
            await _database.Client.RefreshAsync();

            var result = await _repository.GetByAgeAsync(employee19.Age);
            Assert.Equal(employee19.ToJson(), result.ToJson());

            var results = await _repository.GetAllByAgeAsync(employee20.Age);
            Assert.Equal(1, results.Total);
            Assert.Equal(employee20.ToJson(), results.Documents.First().ToJson());
        }

        [Fact]
        public async Task GetByCompanyAsync() {
            await RemoveDataAsync();

            var employee1 = await _repository.AddAsync(EmployeeGenerator.Generate(age: 19, companyId: EmployeeGenerator.DefaultCompanyId));
            var employee2 = await _repository.AddAsync(EmployeeGenerator.Generate(age: 20));
            await _database.Client.RefreshAsync();

            var result = await _repository.GetByCompanyAsync(employee1.CompanyId);
            Assert.Equal(employee1.ToJson(), result.ToJson());

            var results = await _repository.GetAllByCompanyAsync(employee1.CompanyId);
            Assert.Equal(1, results.Total);
            Assert.Equal(employee1.ToJson(), results.Documents.First().ToJson());
            
            Assert.Equal(1, await _repository.GetCountByCompanyAsync(employee1.CompanyId));
            await _repository.RemoveAsync(employee1, false);
            await _database.Client.RefreshAsync();
            Assert.Equal(1, await _repository.CountAsync());
            Assert.Equal(0, await _repository.GetCountByCompanyAsync(employee1.CompanyId));
        }

        private async Task RemoveDataAsync() {
            await _cache.RemoveAllAsync();
            _database.Delete();
            _database.Configure();
            await _database.Client.RefreshAsync();
        }
    }
}