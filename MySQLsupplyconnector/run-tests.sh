#!/bin/sh
sudo docker run --name mysql-database -p 3300:3306 -e MYSQL_ROOT_PASSWORD=mysqlcontainer123 -e MYSQL_DATABASE=mysql -d mysql:latest
sleep 10
sudo docker exec -i mysql-database mysql -uroot -pmysqlcontainer123 mysql < MySQLSupplyCollectorTests/tests/data.sql
dotnet test
sudo docker stop mysql-database
sudo docker rm mysql-database