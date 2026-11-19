using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using OpenDCP.Framework.Configuration.Model.Extensions;
using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Configuration.Model.Requests;
using OpenDCP.Framework.Connections.Datasource.MSSQL.Extensions;
using OpenDCP.Framework.Datasource.Base;
using OpenDCP.Framework.Exceptions;
using OpenDCP.Framework.FormBuilder.Constants;
using OpenDCP.Framework.FormBuilder.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDCP.Framework.Connections.Datasource.MSSQL
{
    public abstract class MSSQLConnection : BaseDatasource, IRelationDataSource
    {
        protected MSSQLConnection(IConfiguration configuration) : base(configuration)
        {
        }

        protected MSSQLConnection(int? id, JsonObject connectionString, IConfiguration configuration) : base(id, connectionString, configuration)
        {
        }

        protected static string GetFormattedConnectionString(JsonObject connectionString)
        {
            return
                $"data source={GetMandatoryField(connectionString, "Server")};" +
                $"initial catalog={GetMandatoryField(connectionString, "Database")};" +
                $"persist security info=True;" +
                $"User ID={GetMandatoryField(connectionString, "User")};" +
                $"Password={GetMandatoryField(connectionString, "Password")};" +
                $"TrustServerCertificate=True;" +
                $"application name=DCP";
        }

        protected abstract List<InputField> SchemaConnectionStringFields { get; }

        protected override List<string> SecretFields { get; } = new List<string> { "password" };

        protected override List<string> SecretDistributionFields { get; } = new List<string> { "password", "user", "database", "server" };

        protected override List<InputField> ConnectionStringFields
        {
            get
            {
                var connectionFields = new List<InputField>
                    {
                        new InputField
                        {
                            FieldType = (byte)InputFieldType.String,
                            Name = "Server",
                            Label = "Server",
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToName()
                                }
                            }
                        },
                        new InputField
                        {
                            FieldType = (byte)InputFieldType.Number,
                            Name = "Port",
                            Label = "Port",
                            FieldSettings = new FieldSettings
                            {
                                DefaultValue = 1443,
                                StepSize = 1
                            },
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToName()
                                }
                            }
                        },
                        new InputField
                        {
                            FieldType = (byte)InputFieldType.String,
                            Name = "Database",
                            Label = "Database",
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToName()
                                }
                            }
                        },
                        new InputField
                        {
                            FieldType = (byte)InputFieldType.String,
                            Name = "User",
                            Label = "User",
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToName()
                                }
                            }
                        },
                        new InputField
                        {
                            FieldType = (byte)InputFieldType.Secret,
                            Name = "Password",
                            Label = "Password",
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToName()
                                }
                            }
                        }
                    };
                connectionFields.AddRange(SchemaConnectionStringFields);
                return connectionFields;
            }

        }
        public override string DisplayConnectionString()
        {
            return $"Database: {GetMandatoryField(_connectionString, "Database")} at {GetMandatoryField(_connectionString, "Server")}";
        }
        protected abstract Task ValidateDatabaseSchema(SqlConnection connection, JsonObject connectionString);
        public async Task ValidateConnectionString(JsonObject connectionString)
        {
            try
            {

                var server = connectionString["Server"].ToString();
                var database = connectionString["Database"].ToString();
                var user = connectionString["User"].ToString();
                var password = connectionString["Password"].ToString();

                //ToDo Using the port in connection string causes the remote server to refuse the connection
                //var port = sql.Value<string>("Port");

                var connectionStringBuilt = string.Format("data source={0};initial catalog={1};persist security info=True;User ID={2};Password={3};application name=DCP;Encrypt=false;TrustServerCertificate=true", server, database, user, password);

                using (var connection = new SqlConnection(connectionStringBuilt))
                {
                    await connection.OpenAsync();

                    await ValidateDatabaseSchema(connection, connectionString);
                }
            }
            catch (DCPBadRequestException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new DCPBadRequestException("Connection to database failed");
            }
        }

        protected dynamic ConvertSQLValueString(string valueString, string sqlType)
        {
            dynamic convertedValue = null;
            bool isNA = valueString == "NA";

            switch (sqlType)
            {
                case "varchar":
                case "nvarchar":
                case "char":
                case "text":
                    convertedValue = valueString;
                    break;
                case "int":
                    convertedValue = isNA ? 0 : int.Parse(valueString);
                    break;
                case "bigint":
                    convertedValue = isNA ? 0L : long.Parse(valueString);
                    break;
                case "float":
                    convertedValue = isNA ? float.NaN : float.Parse(valueString);
                    break;
                case "decimal":
                    convertedValue = isNA ? 0m : decimal.Parse(valueString);
                    break;
                case "datetime":
                    convertedValue = isNA ? new DateTime() : DateTime.Parse(valueString);
                    break;
                case "bit":
                    convertedValue = isNA ? false : bool.Parse(valueString);
                    break;
                // Handle other SQL data types as needed
                default:
                    throw new NotSupportedException($"Conversion for SQL data type '{sqlType}' not supported.");
            }

            return convertedValue;
        }
        protected static string GetMandatoryField(JsonObject obj, string key)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            return obj[key]?.ToString() ?? throw new DCPBadRequestException("Missing field: " + key);
        }

        public async Task<Dictionary<string, List<string>>> FindByQueryAsync(string query, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, List<string>>();

            Func<DbDataReader, KeyValuePair<string, string>> mappingFunction = reader =>
            {
                var key = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
                var value = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();
                return new KeyValuePair<string, string>(key, value);
            };

            IList<KeyValuePair<string, string>> rows;

            using (var connection = new SqlConnection(GetFormattedConnectionString(_connectionString)))
            {
                rows = await connection.SqlQueryAsync(
                    CommandType.Text,
                    query,
                    parameters: null,
                    mappingFunc: mappingFunction);
            }

            foreach (var pair in rows)
            {
                if (pair.Key == null) continue;

                if (!result.TryGetValue(pair.Key, out var list))
                {
                    list = new List<string>();
                    result[pair.Key] = list;
                }

                if (pair.Value != null)
                {
                    list.Add(pair.Value);
                }
            }

            return result;
        }

        public async Task<IList<T>> ExecuteSqlAsync<T>(SqlQueryRequest sqlQueryRequest, CancellationToken cancellationToken = default) where T : new()
        {
            var sqlParameters = sqlQueryRequest.QueryParameters
                .Select(x => new SqlParameter
                {
                    ParameterName = x.Key,
                    Value = x.Value,
                    DbType = DbType.String,
                }).ToList();

            IList<T> resultFromDb;

            using (var connection = new SqlConnection(GetFormattedConnectionString(_connectionString)))
            {
                resultFromDb = await connection.SqlQueryAsync<T>(CommandType.Text, sqlQueryRequest.Query, sqlParameters);
            }

            return resultFromDb;
        }
    }
}
