using MySql.Data.MySqlClient;
using S2.BlackSwan.SupplyCollector;
using S2.BlackSwan.SupplyCollector.Models;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace MySQLsupplyconnector
{
    public class MySQLsupplyconnector : SupplyCollectorBase
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
                        "select t.table_schema, t.table_name, pg_relation_size('\"' || t.table_schema || '\".\"' || t.table_name || '\"') as size, s.n_live_tup, s.n_dead_tup\n" +
                        "from information_schema.tables t\n" +
                        "left outer join pg_stat_user_tables s on s.schemaname = t.table_schema and s.relname = t.table_name\n" +
                        "where table_schema not in ('pg_catalog', 'information_schema')";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int column = 0;

                            var schema = reader.GetString(column++);
                            var table = reader.GetString(column++);
                            var size = reader.GetInt64(column++);
                            var liveRows = reader.GetInt64(column++);
                            var deadRows = reader.GetInt64(column++);

                            var totalRows = liveRows + deadRows;

                            var deadSize = totalRows == 0 ? 0 : ((double)size / (liveRows + deadRows)) * deadRows;

                            metrics.Add(new DataCollectionMetrics()
                            {
                                Schema = schema,
                                Name = table,
                                RowCount = liveRows,
                                TotalSpaceKB = size / 1024,
                                UnUsedSpaceKB = (long)(deadSize / 1024),
                                UsedSpaceKB = (long)(size - deadSize) / 1024
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
                        "select c.table_schema, c.table_name, c.column_name, c.data_type, c.column_default, c.is_nullable, c.is_identity,\n" +
                        "(select count(*)\n" +
                        "   from information_schema.constraint_column_usage ccu\n" +
                        "   join information_schema.table_constraints tc on ccu.constraint_name = tc.constraint_name and ccu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'PRIMARY KEY'\n" +
                        "   where ccu.table_schema = c.table_schema and ccu.table_name = c.table_name and ccu.column_name = c.column_name\n" +
                        ") as is_primary,\n" +
                        "(select count(*)\n" +
                        "   from information_schema.constraint_column_usage ccu\n" +
                        "   join information_schema.table_constraints tc on ccu.constraint_name = tc.constraint_name and ccu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'UNIQUE'\n" +
                        "   where ccu.table_schema = c.table_schema and ccu.table_name = c.table_name and ccu.column_name = c.column_name\n" +
                        ") as is_unique,\n" +
                        "(select count(*)\n" +
                        "   from information_schema.key_column_usage kcu\n" +
                        "   join information_schema.table_constraints tc on kcu.constraint_name = tc.constraint_name and kcu.constraint_schema = tc.constraint_schema and tc.constraint_type = 'FOREIGN KEY'\n" +
                        "   where kcu.table_schema = c.table_schema and kcu.table_name = c.table_name and kcu.column_name = c.column_name\n" +
                        ") as is_ref\n" +
                        "from information_schema.columns c\n" +
                        "where c.table_schema not in ('pg_catalog', 'information_schema')\n" +
                        "order by table_schema, table_name, ordinal_position";

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
                            var isPrimary = reader.GetInt64(column++) > 0;
                            var isUnique = reader.GetInt64(column++) > 0;
                            var isRef = reader.GetInt64(column++) > 0;

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
