using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using MySql.Data.MySqlClient;
using S2.BlackSwan.SupplyCollector;
using S2.BlackSwan.SupplyCollector.Models;

namespace MySqlSupplyCollector
{
    public class MySqlSupplyCollector : SupplyCollectorBase
    {
        private MySqlConnection Connect(string connectString)
        {
            var conn = new MySqlConnection(connectString);
            conn.Open();
            /*conn.Open();

            using (var cmd = new MySqlCommand("set net_write_timeout=99999; set net_read_timeout=99999", conn)) {
                cmd.ExecuteNonQuery();
            }*/

            return conn;
        }

        public override List<string> CollectSample(DataEntity dataEntity, int sampleSize)
        {
            var result = new List<string>();
            using (var conn = Connect(dataEntity.Container.ConnectionString))
            {
                long rows = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 600;
                    cmd.CommandText =
                        $"SELECT COUNT(*) FROM {dataEntity.Collection.Name}";

                    rows = (long)cmd.ExecuteScalar();
                }


                using (var cmd = conn.CreateCommand())
                {
                    string sampling = "";
                    if (rows > 0)
                    {
                        double pct = 0.2 + (double)sampleSize / rows;
                        if (pct >= 1)
                            pct = 0.999;

                        sampling = $"WHERE RAND() < {pct}".Replace(",", ".");
                    }

                    cmd.CommandTimeout = 600;
                    //
                    cmd.CommandText = $"SELECT {dataEntity.Name} FROM {dataEntity.Collection.Name} {sampling} LIMIT {sampleSize}";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var val = reader[0];
                            if (val is DBNull)
                            {
                                result.Add(null);
                            }
                            else
                            {
                                result.Add(val.ToString());
                            }
                        }
                    }
                }
            }

            return result.Take(sampleSize).ToList();
        }

        public override List<string> DataStoreTypes()
        {
            return (new[] { "MySql" }).ToList();
        }

        public string BuildConnectionString(string user, string password, string database, string host, int port = 3306)
        {
            return $"server={host}; Port={port}; uid={user}; pwd={password}; database={database};Connection Timeout=300";
        }

        public override List<DataCollectionMetrics> GetDataCollectionMetrics(DataContainer container)
        {
            var metrics = new List<DataCollectionMetrics>();
            using (var conn = Connect(container.ConnectionString))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 600;
                    cmd.CommandText =
                        @" select c.table_schema, c.table_name, (c.data_length + c.index_length) as size, c.TABLE_ROWS as liveRows, 
                             (select SUM(DATA_FREE)
                                FROM  INFORMATION_SCHEMA.PARTITIONS P
                                WHERE P.TABLE_SCHEMA = c.table_schema
                                AND   P.TABLE_NAME   = c.table_name) as UnusedSpace
	                            from information_schema.tables as c where c.table_schema = """ + conn.Database + "\""; /*not in ('sys', 'information_schema', 'performance_schema')";*/

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var schema = reader.GetString(0);
                            var table = reader.GetString(1);
                            long size = 0;
                            if (!reader.IsDBNull(2))
                            {
                                size = reader.GetInt64(2);
                            }
                            long liveRows = 0;
                            if (!reader.IsDBNull(3))
                            {
                                liveRows = reader.GetInt64(3);
                            }
                            long unusedSize = 0;
                            if (!reader.IsDBNull(4))
                            {
                                unusedSize = reader.GetInt64(4);
                            }

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

            using (var conn = Connect(container.ConnectionString))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 600;
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
                        where c.table_schema  = """ + conn.Database + "\""/*not in ('pg_catalog', 'information_schema')*/ +
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
                using (Connect(container.ConnectionString))
                {
                    // nothing
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #region SpeedScanner Support
        public override long? CountAllEntities(DataCollection collection)
        {
            using (var conn = Connect(collection.Container.ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = 600;
                cmd.CommandText = $"select count(*) from {collection.Schema}.{collection.Name} ";
                return (long)cmd.ExecuteScalar();
            }
        }

        public override DataTable GetDataTableSamples(DataCollection collection, int sampleSize)
        {
            using (var conn = Connect(collection.Container.ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = 600;
                cmd.CommandText = $"SELECT * FROM {collection.Name} LIMIT {sampleSize}";

                var dataTable = new DataTable();
                var dataAdapter = new MySqlDataAdapter(cmd);
                dataAdapter.Fill(dataTable);
                return dataTable;
            }
        }
        #endregion

        private DataType ConvertDataType(string dbDataType)
        {
            switch(dbDataType)
            {
                case "bigint":
                case "integer":
                case "int":
                    return DataType.Long;
                case "smallint":
                case "tinyint":
                    return DataType.Short;
                case "boolean":
                case "bool":
                case "bit":
                    return DataType.Boolean;
                case "char":
                    return DataType.Char;
                case "varchar":
                case "longtext":
                case "mediumtext":
                case "text":
                case "json":
                    return DataType.String;
                case "double":
                case "real":
                    return DataType.Double;
                case "decimal":
                case "numeric":
                    return DataType.Decimal;
                case "date":
                case "datetime":
                    return DataType.DateTime;
                case "uuid":
                    return DataType.Guid;
                case "longblob":
                case "binary":
                    return DataType.ByteArray;
                default:
                    return DataType.Unknown;
            }
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
