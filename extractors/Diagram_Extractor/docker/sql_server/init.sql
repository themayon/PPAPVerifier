-- init.sql
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'CeleryTaskDb')
BEGIN
    CREATE DATABASE CeleryTaskDb;
END
GO

USE CeleryTaskDb;
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'example_table')
BEGIN
    CREATE TABLE example_table (
        id INT PRIMARY KEY,
        name NVARCHAR(100)
    );
END
GO
