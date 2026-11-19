using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace OpenDCP.Framework.Connections.Datasource.MSSQL.Extensions
{
    public static class SqlConnectionExtensions
    {
        public static void AddINParamsToCommand(this DbCommand cmd, Dictionary<string, List<string>> parameters)
        {
            foreach (var item in parameters)
            {
                string[] parameterValues = item.Value.Select((paramText) => paramText.ToString()).ToArray();

                string[] parameterNames = parameterValues.Select(
                    (paramText, paramNumber) => "@" + item.Key + paramNumber.ToString()).ToArray();

                for (int i = 0; i < parameterNames.Length; i++)
                {
                    DbParameter p = cmd.CreateParameter();
                    p.ParameterName = parameterNames[i];
                    p.Value = parameterValues[i];
                    cmd.Parameters.Add(p);
                }
            }
        }
        public static async Task<IList<T>> SqlQueryAsync<T>(this SqlConnection connection, CommandType type, string sql, List<SqlParameter> parameters, Dictionary<string, List<string>> inParameters = null) where T : new()
        {
            if (connection.State == ConnectionState.Closed)
                await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandType = type;
                if (parameters != null && parameters.Count() > 0)
                {
                    foreach (var item in parameters)
                    {
                        DbParameter p = command.CreateParameter();
                        p.DbType = item.DbType;
                        p.ParameterName = item.ParameterName;
                        p.Value = item.Value;
                        command.Parameters.Add(p);
                    }
                }
                if (inParameters != null && inParameters.Count() > 0)
                {
                    command.AddINParamsToCommand(inParameters);
                }
                var propts = typeof(T).GetProperties();
                var rtnList = new List<T>();
                T model;
                object val;
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        model = new T();
                        foreach (var l in propts)
                        {

                            if (Enumerable.Range(0, reader.FieldCount).Any(i => string.Equals(reader.GetName(i), l.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                val = reader[l.Name];
                                if (val == DBNull.Value)
                                    l.SetValue(model, null);
                                else
                                    l.SetValue(model, val);
                            }

                        }
                        rtnList.Add(model);
                    }
                }
                return rtnList;
            }
        }

        public static IList<T> SqlQuery<T>(this SqlConnection connection, CommandType type, string sql, List<SqlParameter> parameters, Dictionary<string, List<string>> inParameters = null) where T : new()
        {

            if (connection.State == ConnectionState.Closed)
                connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandType = type;
                if (parameters != null && parameters.Count() > 0)
                {
                    foreach (var item in parameters)
                    {
                        DbParameter p = command.CreateParameter();
                        p.DbType = item.DbType;
                        p.ParameterName = item.ParameterName;
                        p.Value = item.Value;
                        command.Parameters.Add(p);
                    }
                }
                if (inParameters != null && inParameters.Count() > 0)
                {
                    command.AddINParamsToCommand(inParameters);
                }
                var propts = typeof(T).GetProperties();
                var rtnList = new List<T>();
                T model;
                object val;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        model = new T();
                        foreach (var l in propts)
                        {

                            if (Enumerable.Range(0, reader.FieldCount).Any(i => string.Equals(reader.GetName(i), l.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                val = reader[l.Name];
                                if (val == DBNull.Value)
                                    l.SetValue(model, null);
                                else
                                    l.SetValue(model, val);
                            }

                        }
                        rtnList.Add(model);
                    }
                }
                return rtnList;
            }
        }

        public static async Task<IList<T>> SqlQueryAsync<T>(
            this SqlConnection connection,
            CommandType type,
            string sql,
            List<SqlParameter> parameters,
            Func<DbDataReader, T> mappingFunc,
            Dictionary<string, List<string>> inParameters = null)
        {
            if (connection.State == ConnectionState.Closed)
                await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandType = type;

                if (parameters != null && parameters.Count > 0)
                {
                    foreach (var item in parameters)
                    {
                        DbParameter p = command.CreateParameter();
                        p.DbType = item.DbType;
                        p.ParameterName = item.ParameterName;
                        p.Value = item.Value;
                        command.Parameters.Add(p);
                    }
                }

                if (inParameters != null && inParameters.Count > 0)
                {
                    command.AddINParamsToCommand(inParameters);
                }

                var rtnList = new List<T>();

                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        rtnList.Add(mappingFunc(reader));
                    }
                }

                return rtnList;
            }
        }
    }
}
