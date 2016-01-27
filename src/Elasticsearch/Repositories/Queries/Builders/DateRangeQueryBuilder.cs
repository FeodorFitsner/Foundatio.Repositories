﻿using System;
using System.Linq;
using Nest;

namespace Foundatio.Elasticsearch.Repositories.Queries.Builders {
    public class DateRangeQueryBuilder : QueryBuilderBase {
        public override void BuildFilter<T>(object query, object options, ref FilterContainer container) {
            var dateRangeQuery = query as IDateRangeQuery;
            if (dateRangeQuery?.DateRanges == null || dateRangeQuery.DateRanges.Count <= 0)
                return;

            foreach (var dateRange in dateRangeQuery.DateRanges.Where(dr => dr.UseDateRange)) {
                container &= new RangeFilter {
                    Field = dateRange.Field,
                    GreaterThanOrEqualTo = dateRange.GetStartDate().ToString("o"),
                    LowerThanOrEqualTo = dateRange.GetEndDate().ToString("O")
                };
            }
        }
    }
}