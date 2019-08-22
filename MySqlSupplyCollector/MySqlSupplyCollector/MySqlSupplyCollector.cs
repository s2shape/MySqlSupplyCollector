using MySql.Data.MySqlClient;
using S2.BlackSwan.SupplyCollector;
using S2.BlackSwan.SupplyCollector.Models;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace MySqlSupplyCollector
{
    public class MySqlSupplyCollector : SupplyCollectorBase
    {
        public override List<string> CollectSample(DataEntity dataEntity, int sampleSize)
        {
            var result = new List<string>();
            using (var conn = new MySqlConnection(dataEntity.Container.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT {dataEntity.Name} FROM {dataEntity.Collection.Name} LIMIT {sampleSize}";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var val = reader[0];
                            if (val is DBNull)
                            {
                                result.Add(null);
                            }
                            else if (val.GetType().IsArray)
                            {
                                var arr = (Array)val;
                                var sb = new StringBuilder();
                                for (int i = 0; i < arr.Length; i++)
                                {
                                    if (sb.Length > 0)
                                        sb.Append(",");
                                    sb.Append(arr.GetValue(i).ToString())
;
                                }
                                result.Add(sb.ToString());
                            }
                            else
                            {
                                result.Add(val.ToString());
                            }
                        }
                    }
                }
            }

            return result;
        }

        public override List<string> DataStoreTypes()
        {
            return (new[] { "MySQL" }).ToList();
        }

        public string BuildConnectionString(string user, string password, string database, string host, int port = 3300)
        {
           return $"server={host}; Port={port}; uid={user}; pwd={password}; database={database}";
        }

        public override List<DataCollectionMetrics> GetDataCollectionMetrics(DataContainer container)
        {
            var metrics = new List<DataCollectionMetrics>();
            using (var conn = new MySqlConnection(container.ConnectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @" select c.table_schema, c.table_name, (c.data_length + c.index_length) as size, c.TABLE_ROWS as liveRows, 
                             (select SUM(DATA_FREE)
                                FROM  INFORMATION_SCHEMA.PARTITIONS P
                                WHERE P.TABLE_SCHEMA = c.table_schema
                                AND   P.TABLE_NAME   = c.table_name) as UnusedSpace
	                            from information_schema.tables as c where c.table_schema not in ('sys', 'information_schema', 'performance_schema')";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int column = 0;

                            var schema = reader.GetString(column++);
                            var table = reader.GetString(column++);
                            var size = reader.GetInt64(column++);
                            var liveRows = reader.GetInt64(column++);
                            var unusedSize = reader.GetInt64(column++);

                            metrics.Add(new DataCollectionMetrics()
                            {
                                Schema = schema,
                                Name = table,
                                RowCount = liveRows,
                                TotalSpaceKB = (long)((size + unusedSize) / 1024),
                                UnUsedSpaceKB = (long)(unusedSize / 1024),
                                UsedSpaceKB = (long)(size) / 1024
                            });
                        }
                    }
                }
            }

            return metrics;
        }

        public override (List<DataCollection>, List<DataEntity>) GetSchema(DataContainer container)
        {
            var collections = new List<DataCollection>();
            var entities = new List<DataEntity>();

            using (var conn = new MySqlConnection(container.ConnectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                       @"select c.table_schema, c.table_name, c.column_name, c.data_type, c.column_default, c.is_nullable, c.extra,(select if(c.extra = 'auto_increment', 'YES', 'NO')) as  is_identity,
                        (select count(*)
                           from information_schema.KEY_COLUMN_USAGE ccu
                           join information_schema.table_constraints tc on ccu.constraint_name = tc.constraint_name and ccu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'PRIMARY KEY'
                           where ccu.table_schema = c.table_schema and ccu.table_name = c.table_name and ccu.column_name = c.column_name
                        ) as is_primary,
                        (select count(*)
                           from information_schema.KEY_COLUMN_USAGE ccu
                           join information_schema.table_constraints tc on ccu.constraint_name = tc.constraint_name and ccu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'UNIQUE'
                           where ccu.table_schema = c.table_schema and ccu.table_name = c.table_name and ccu.column_name = c.column_name
                        ) as is_unique,
                        (select count(*)
                           from information_schema.key_column_usage kcu
                           join information_schema.table_constraints tc on kcu.constraint_name = tc.constraint_name and kcu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'FOREIGN KEY'
                           where kcu.table_schema = c.table_schema and kcu.table_name = c.table_name and kcu.column_name = c.column_name
                        ) as is_ref
                        from information_schema.columns c
                        where c.table_schema not in ('pg_catalog', 'information_schema')
                        order by table_schema, table_name, ordinal_position";

                    DataCollection collection = null;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int column = 0;

                            var schema = reader.GetDbString(column++);
                            var table = reader.GetDbString(column++);
                            var columnName = reader.GetDbString(column++);
                            var dataType = reader.GetDbString(column++);
                            var columnDef = reader.GetDbString(column++);
                            var isNullable = "YES".Equals(reader.GetDbString(column++), StringComparison.InvariantCultureIgnoreCase);
                            var isIdentity = "YES".Equals(reader.GetDbString(column++), StringComparison.InvariantCultureIgnoreCase);
                            var isPrimary = reader.GetInt64(8) > 0;
                            var isUnique = reader.GetInt64(9) > 0;
                            var isRef = reader.GetInt64(10) > 0;

                            if (collection == null || !collection.Schema.Equals(schema) ||
                                !collection.Name.Equals(table))
                            {

                                collection = new DataCollection(container, table)
                                {
                                    Schema = schema
                                };
                                collections.Add(collection);
                            }

                            entities.Add(new DataEntity(columnName, ConvertDataType(dataType), dataType, container, collection)
                            {
                                IsAutoNumber = !String.IsNullOrEmpty(columnDef) && columnDef.StartsWith("nextval(", StringComparison.InvariantCultureIgnoreCase),
                                IsComputed = !String.IsNullOrEmpty(columnDef),
                                IsForeignKey = isRef,
                                IsIndexed = isPrimary || isRef,
                                IsPrimaryKey = isPrimary,
                                IsUniqueKey = isUnique
                            });
                        }
                    }
                }
            }

            return (collections, entities);
        }

        public override bool TestConnection(DataContainer container)
        {
            try
            {
                using (var conn = new MySqlConnection(container.ConnectionString))
                {
                    conn.Open();
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private DataType ConvertDataType(string dbDataType)
        {
            if ("integer".Equals(dbDataType))
            {
                return DataType.Long;
            }
            else if ("smallint".Equals(dbDataType))
            {
                return DataType.Short;
            }
            else if ("boolean".Equals(dbDataType))
            {
                return DataType.Boolean;
            }
            else if ("character".Equals(dbDataType))
            {
                return DataType.Char;
            }
            else if ("character varying".Equals(dbDataType))
            {
                return DataType.String;
            }
            else if ("text".Equals(dbDataType))
            {
                return DataType.String;
            }
            else if ("double precision".Equals(dbDataType))
            {
                return DataType.Double;
            }
            else if ("real".Equals(dbDataType))
            {
                return DataType.Double;
            }
            else if ("numeric".Equals(dbDataType))
            {
                return DataType.Decimal;
            }
            else if ("date".Equals(dbDataType))
            {
                return DataType.DateTime;
            }
            else if ("time without time zone".Equals(dbDataType))
            {
                return DataType.DateTime;
            }
            else if ("time with time zone".Equals(dbDataType))
            {
                return DataType.DateTime;
            }
            else if ("timestamp without time zone".Equals(dbDataType))
            {
                return DataType.DateTime;
            }
            else if ("timestamp with time zone".Equals(dbDataType))
            {
                return DataType.DateTime;
            }
            else if ("json".Equals(dbDataType))
            {
                return DataType.String;
            }
            else if ("uuid".Equals(dbDataType))
            {
                return DataType.Guid;
            }

            return DataType.Unknown;
        }
    }

    internal static class DbDataReaderExtensions
    {
        internal static string GetDbString(this DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;
            return reader.GetString(ordinal);
        }
    }
}
