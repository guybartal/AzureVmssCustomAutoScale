using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using vmssAutoScale.Interfaces;

namespace vmssAutoScale.SqlLoadWatcher
{
    public class SqlLoadWatcher : ILoadWatcher
    {
        public double GetCurrentLoad()
        {
            var sqlConnectionString = ConfigurationManager.AppSettings["SQLConnectionString"];

            string queryString = "exec GetAverageLoad";
            using (SqlConnection connection = new SqlConnection(sqlConnectionString))
            {
                SqlCommand command = new SqlCommand(queryString, connection);
                connection.Open();
                double value;

                var scalar = command.ExecuteScalar();

                if (Double.TryParse(scalar.ToString(), out value))
                {
                    return value;
                }
                else
                {
                    throw new Exception("Failed get average load from sql server");
                }
            }
        }
    }
}
