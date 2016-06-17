﻿using System;
using System.Collections.Generic;
using Nest;

namespace Foundatio.Repositories.Elasticsearch.Queries {
    public class ElasticQuery : Query, IElasticFilterQuery, IElasticIndicesQuery {
        public ElasticQuery() {
            Indices = new List<String>();
        }

        public FilterContainer ElasticFilter { get; set; }
        public List<string> Indices { get; set; }
    }
}
