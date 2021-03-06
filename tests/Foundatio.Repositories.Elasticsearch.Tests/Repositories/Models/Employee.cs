﻿using System;
using System.Collections.Generic;
using Exceptionless;
using Foundatio.Repositories.Models;
using Foundatio.Repositories.Utility;

namespace Foundatio.Repositories.Elasticsearch.Tests.Models {
    public class Employee : IIdentity, IHaveDates, IVersioned, ISupportSoftDeletes {
        public string Id { get; set; }
        public string CompanyId { get; set; }
        public string CompanyName { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public int YearsEmployed { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public long Version { get; set; }
        public bool IsDeleted { get; set; }

        protected bool Equals(Employee other) {
            return String.Equals(Id, other.Id, StringComparison.InvariantCultureIgnoreCase) && 
                String.Equals(CompanyId, other.CompanyId, StringComparison.InvariantCultureIgnoreCase) && 
                String.Equals(CompanyName, other.CompanyName, StringComparison.InvariantCultureIgnoreCase) && 
                String.Equals(Name, other.Name, StringComparison.InvariantCultureIgnoreCase) && 
                Age == other.Age &&
                YearsEmployed == other.YearsEmployed &&
                CreatedUtc.Equals(other.CreatedUtc) && 
                UpdatedUtc.Equals(other.UpdatedUtc) && 
                Version == other.Version;
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((Employee)obj);
        }
        
        public override int GetHashCode() {
            unchecked {
                var hashCode = (Id != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(Id) : 0);
                hashCode = (hashCode * 397) ^ (CompanyId != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(CompanyId) : 0);
                hashCode = (hashCode * 397) ^ (CompanyName != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(CompanyName) : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(Name) : 0);
                hashCode = (hashCode * 397) ^ Age;
                hashCode = (hashCode * 397) ^ YearsEmployed;
                hashCode = (hashCode * 397) ^ CreatedUtc.GetHashCode();
                hashCode = (hashCode * 397) ^ UpdatedUtc.GetHashCode();
                hashCode = (hashCode * 397) ^ Version.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(Employee left, Employee right) {
            return Equals(left, right);
        }

        public static bool operator !=(Employee left, Employee right) {
            return !Equals(left, right);
        }
    }

    public static class EmployeeGenerator {
        public static readonly string DefaultCompanyId = ObjectId.GenerateNewId().ToString();

        public static Employee Default => new Employee {
            Name = "Blake",
            Age = 29,
            YearsEmployed = 9,
            CompanyName = "Exceptionless",
            CompanyId = DefaultCompanyId
        };

        public static Employee Generate(string id = null, string name = null, int? age = null, int? yearsEmployed = null, string companyName = null, string companyId = null, DateTime? createdUtc = null, DateTime? updatedUtc = null) {
            return new Employee {
                Id = id,
                Name = name,
                Age = age ?? RandomData.GetInt(18, 100),
                YearsEmployed = yearsEmployed ?? RandomData.GetInt(0, 1),
                CompanyName = companyName,
                CompanyId = companyId ?? ObjectId.GenerateNewId().ToString(),
                CreatedUtc = createdUtc.GetValueOrDefault(),
                UpdatedUtc = updatedUtc.GetValueOrDefault()
            };
        }

        public static List<Employee> GenerateEmployees(int count = 10, string id = null, string name = null, int? age = null, int? yearsEmployed = null, string companyName = null, string companyId = null, DateTime? createdUtc = null, DateTime? updatedUtc = null) {
            var results = new List<Employee>(count);
            for (int index = 0; index < count; index++)
                results.Add(Generate(id, name, age, yearsEmployed, companyName, companyId, createdUtc, updatedUtc));

            return results;
        }
    }
}

