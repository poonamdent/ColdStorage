using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ColdStorage.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public DashboardModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty(SupportsGet = true)]
        public string? State { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? City { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        public List<StorageSummary> StorageData { get; set; } = new();
        public List<string> States { get; set; } = new();
        public List<string> Cities { get; set; } = new();

        public void OnGet()
        {
            LoadFiltersAndData();
        }

        private void LoadFiltersAndData()
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                // Get all tables from the database
                DataTable schema = conn.GetSchema("Tables");

                // Build dynamic query to aggregate data from all relevant tables
                string summaryQuery = BuildSummaryQuery(conn);

                // Load the aggregated data
                using (SqlCommand cmd = new SqlCommand(summaryQuery, conn))
                {
                    // Add filter parameters
                    if (!string.IsNullOrEmpty(State))
                        cmd.Parameters.AddWithValue("@State", State);
                    if (!string.IsNullOrEmpty(City))
                        cmd.Parameters.AddWithValue("@City", City);
                    if (StartDate.HasValue)
                        cmd.Parameters.AddWithValue("@StartDate", StartDate.Value);
                    if (EndDate.HasValue)
                        cmd.Parameters.AddWithValue("@EndDate", EndDate.Value);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            StorageData.Add(new StorageSummary
                            {
                                Id = GetSafeInt(reader, "Id"),
                                SurveyId = GetSafeString(reader, "SurveyId"),
                                State = GetSafeString(reader, "State"),
                                City = GetSafeString(reader, "City"),
                                Location = GetSafeString(reader, "Location"),
                                ActualCapacity = GetSafeDecimal(reader, "ActualCapacity"),
                                TotalArea = GetSafeDecimal(reader, "TotalArea"),
                                Used = GetSafeDecimal(reader, "Used"),
                                Available = GetSafeDecimal(reader, "Available"),
                                Status = GetSafeString(reader, "Status"),
                                Temperature = GetSafeDecimal(reader, "Temperature"),
                                SurveyDate = GetSafeDateTime(reader, "SurveyDate"),
                                Latitude = GetSafeDouble(reader, "Latitude"),
                                Longitude = GetSafeDouble(reader, "Longitude")
                            });
                        }
                    }
                }

                // Extract unique states and cities from loaded data
                States = StorageData.Select(s => s.State)
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Distinct()
                    .OrderBy(r => r)
                    .ToList();

                Cities = StorageData.Select(s => s.City)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();
            }
        }

        private string BuildSummaryQuery(SqlConnection conn)
        {
            string query = @"
                SELECT 
                    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) as Id,
                    [Survey ID] as SurveyId,
                    COALESCE([State], 'Unknown') as State,
                    COALESCE([City], 'Unknown') as City,
                    COALESCE([State], 'Unknown') as Location,
                    [What is the actual capacity of your facility (in metric tonnes)?] as ActualCapacity,
                    [What is the total area of this facility (in sq# mt)] as TotalArea,
                    [Observation Date] as SurveyDate,
 'Active' as Status,
                    [Latitude] as Latitude,
                    [Longitude] as Longitude
                FROM Cold_Storage
                WHERE 1=1";

            // Add filter conditions
            if (!string.IsNullOrEmpty(State))
                query += " AND [State] = @State";
            if (!string.IsNullOrEmpty(City))
                query += " AND [City] = @City";
            if (StartDate.HasValue)
                query += " AND [Observation Date] >= @StartDate";
            if (EndDate.HasValue)
                query += " AND [Observation Date] <= @EndDate";

            query += " ORDER BY [State], [City]";

            return query;
        }

        private bool TableExists(SqlConnection conn, string tableName)
        {
            using (SqlCommand cmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName", conn))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                return (int)cmd.ExecuteScalar() > 0;
            }
        }

        private string GetFirstAvailableTable(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand(
                @"SELECT TOP 1 TABLE_NAME 
                  FROM INFORMATION_SCHEMA.TABLES 
                  WHERE TABLE_TYPE = 'BASE TABLE' 
                  AND TABLE_NAME NOT LIKE 'sys%'
                  ORDER BY TABLE_NAME", conn))
            {
                var result = cmd.ExecuteScalar();
                return result?.ToString() ?? "Locations";
            }
        }

        // Safe data extraction methods
        private int GetSafeInt(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
            }
            catch { return 0; }
        }

        private string GetSafeString(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
            }
            catch { return ""; }
        }

        private decimal GetSafeDecimal(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0;

                var fieldType = reader.GetFieldType(ordinal);
                if (fieldType == typeof(decimal))
                    return reader.GetDecimal(ordinal);
                else if (fieldType == typeof(double))
                    return (decimal)reader.GetDouble(ordinal);
                else if (fieldType == typeof(float))
                    return (decimal)reader.GetFloat(ordinal);
                else if (fieldType == typeof(int))
                    return reader.GetInt32(ordinal);

                return Convert.ToDecimal(reader.GetValue(ordinal));
            }
            catch { return 0; }
        }

        private double GetSafeDouble(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0;

                var fieldType = reader.GetFieldType(ordinal);
                if (fieldType == typeof(double))
                    return reader.GetDouble(ordinal);
                else if (fieldType == typeof(decimal))
                    return (double)reader.GetDecimal(ordinal);
                else if (fieldType == typeof(float))
                    return reader.GetFloat(ordinal);

                return Convert.ToDouble(reader.GetValue(ordinal));
            }
            catch { return 0; }
        }

        private DateTime GetSafeDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? DateTime.Now : reader.GetDateTime(ordinal);
            }
            catch { return DateTime.Now; }
        }

        public class StorageSummary
        {
            public int Id { get; set; }
            public string SurveyId { get; set; } = "";
            public string State { get; set; } = "";
            public string City { get; set; } = "";
            public string Location { get; set; } = "";
            public decimal ActualCapacity { get; set; }
            public decimal TotalArea { get; set; }
            public decimal Used { get; set; }
            public decimal Available { get; set; }
            public string Status { get; set; } = "";
            public decimal Temperature { get; set; }
            public DateTime SurveyDate { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }
    }
}