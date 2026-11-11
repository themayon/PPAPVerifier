#!/bin/bash
# entrypoint.sh

# Start SQL Server in the background
/opt/mssql/bin/sqlservr &

# Wait until SQL Server is ready to accept connections
echo "Waiting for SQL Server to start..."
sleep 30  # Adjust the sleep time if necessary, or implement a loop checking readiness

# Run the initialization script
echo "Running initial SQL script..."
/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -i /init.sql

# Bring SQL Server process to foreground so container keeps running
wait
