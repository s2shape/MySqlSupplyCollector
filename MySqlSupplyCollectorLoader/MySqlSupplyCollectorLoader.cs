using System;
using System.IO;
using System.Text;
using MySql.Data.MySqlClient;
using S2.BlackSwan.SupplyCollector.Models;
using SupplyCollectorDataLoader;

namespace MySqlSupplyCollectorLoader
{
    public class MySqlSupplyCollectorLoader : SupplyCollectorDataLoaderBase
    {
        private MySqlConnection Connect(string connectString)
        {
            var conn = new MySqlConnection(connectString);
            conn.Open();

            return conn;
        }

        public override void InitializeDatabase(DataContainer dataContainer) {
            // do nothing
        }

        private MySqlDbType ConvertDbType(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.String:
                    return MySqlDbType.Text;
                case DataType.Int:
                    return MySqlDbType.Int32;
                case DataType.Double:
                    return MySqlDbType.Double;
                case DataType.Boolean:
                    return MySqlDbType.Bit;
                case DataType.DateTime:
                    return MySqlDbType.DateTime;
                default:
                    return MySqlDbType.Int32;
            }
        }

        public override void LoadSamples(DataEntity[] dataEntities, long count)
        {
            using (var conn = Connect(dataEntities[0].Container.ConnectionString))
            {
                var sb = new StringBuilder();
                sb.Append("CREATE TABLE ");
                sb.Append(dataEntities[0].Collection.Name);
                sb.Append("\n");
                sb.Append("id_field serial PRIMARY KEY");

                foreach (var dataEntity in dataEntities)
                {
                    sb.Append(",\n");
                    sb.Append(dataEntity.Name);
                    sb.Append(" ");

                    switch (dataEntity.DataType)
                    {
                        case DataType.String:
                            sb.Append("text");
                            break;
                        case DataType.Int:
                            sb.Append("integer");
                            break;
                        case DataType.Double:
                            sb.Append("double");
                            break;
                        case DataType.Boolean:
                            sb.Append("bit");
                            break;
                        case DataType.DateTime:
                            sb.Append("datetime");
                            break;
                        default:
                            sb.Append("integer");
                            break;
                    }
                }

                sb.Append(");");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sb.ToString();
                    cmd.ExecuteNonQuery();
                }

                sb = new StringBuilder();
                sb.Append("INSERT INTO ");
                sb.Append(dataEntities[0].Collection.Name);
                sb.Append("(");

                bool first = true;
                foreach (var dataEntity in dataEntities)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(dataEntity.Name);
                    first = false;
                }
                sb.Append(") VALUES (");

                first = true;
                foreach (var dataEntity in dataEntities)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append("@");
                    sb.Append(dataEntity.Name);
                    first = false;
                }

                sb.Append(");");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sb.ToString();
                    foreach (var dataEntity in dataEntities)
                    {
                        cmd.Parameters.Add(new MySqlParameter($"@{dataEntity.Name}", ConvertDbType(dataEntity.DataType)));
                    }
                    long rows = 0;

                    var r = new Random();

                    while (rows < count)
                    {
                        foreach (var dataEntity in dataEntities)
                        {
                            object val;

                            switch (dataEntity.DataType)
                            {
                                case DataType.String:
                                    val = new Guid().ToString();
                                    break;
                                case DataType.Int:
                                    val = r.Next();
                                    break;
                                case DataType.Double:
                                    val = r.NextDouble();
                                    break;
                                case DataType.Boolean:
                                    val = r.Next(100) > 50;
                                    break;
                                case DataType.DateTime:
                                    val = DateTimeOffset
                                        .FromUnixTimeMilliseconds(
                                            DateTimeOffset.Now.ToUnixTimeMilliseconds() + r.Next()).DateTime;
                                    break;
                                default:
                                    val = r.Next();
                                    break;
                            }

                            cmd.Parameters[$"@{dataEntity.Name}"].Value = val;
                        }

                        if (rows % 1000 == 0)
                        {
                            Console.Write(".");
                        }

                        cmd.ExecuteNonQuery();

                        rows++;
                    }
                }
            }
        }


        public override void LoadUnitTestData(DataContainer dataContainer) {
            using (var conn = Connect(dataContainer.ConnectionString)) {
                using (var reader = new StreamReader("tests/data.sql")) {
                    var sb = new StringBuilder();
                    while (!reader.EndOfStream) {
                        var line = reader.ReadLine();
                        if(String.IsNullOrEmpty(line))
                            continue;

                        sb.AppendLine(line);
                        if (line.TrimEnd().EndsWith(";")) {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandTimeout = 600;
                                cmd.CommandText = sb.ToString();

                                cmd.ExecuteNonQuery();
                            }

                            sb.Clear();
                        }
                    }
                }
            }
        }
    }
}
