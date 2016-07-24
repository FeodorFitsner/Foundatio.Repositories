﻿using System;
using Exceptionless;
using Exceptionless.DateTimeExtensions;
using Foundatio.Repositories.Models;
using Foundatio.Repositories.Utility;

namespace Foundatio.Repositories.Elasticsearch.Tests.Models {
    public class LogEvent : IIdentity, IHaveCreatedDate {
        public string Id { get; set; }

        protected bool Equals(LogEvent other) {
            return String.Equals(Id, other.Id, StringComparison.InvariantCultureIgnoreCase) && 
                String.Equals(CompanyId, other.CompanyId, StringComparison.InvariantCultureIgnoreCase) && 
                String.Equals(Message, other.Message, StringComparison.InvariantCultureIgnoreCase) && 
                CreatedUtc.Equals(other.CreatedUtc);
        }
        
        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((LogEvent)obj);
        }
        
        public override int GetHashCode() {
            unchecked {
                var hashCode = (Id != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(Id) : 0);
                hashCode = (hashCode * 397) ^ (CompanyId != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(CompanyId) : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(Message) : 0);
                hashCode = (hashCode * 397) ^ CreatedUtc.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(LogEvent left, LogEvent right) {
            return Equals(left, right);
        }

        public static bool operator !=(LogEvent left, LogEvent right) {
            return !Equals(left, right);
        }

        public string CompanyId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public static class LogEventGenerator {
        public static readonly string DefaultCompanyId = ObjectId.GenerateNewId().ToString();

        public static LogEvent Default => new LogEvent {
            Message = "Hello world",
            CompanyId = DefaultCompanyId,
            CreatedUtc = DateTime.Now
        };

        public static LogEvent Generate(string id = null, string companyId = null, string message = null, DateTime? createdUtc = null) {
            return new LogEvent {
                Id = id,
                Message = message ?? RandomData.GetAlphaString(),
                CompanyId = companyId ?? ObjectId.GenerateNewId().ToString(),
                CreatedUtc = createdUtc ?? RandomData.GetDateTime(DateTime.Now.StartOfMonth(), DateTime.Now)
            };
        }
    }
}

