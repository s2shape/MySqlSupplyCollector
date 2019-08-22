#!/bin/sh
docker run --name mysql-database -p 3300:3306 -e MYSQL_ROOT_PASSWORD=mysqlcontainer123 -e MYSQL_DATABASE=mysql -d mysql:latest
sleep 20
docker exec -i mysql-database mysql -uroot -pmysqlcontainer123 mysql < MySqlSupplyCollectorTests/tests/data.sql
sleep 20
dotnet test
docker stop mysql-database
docker rm mysql-database
