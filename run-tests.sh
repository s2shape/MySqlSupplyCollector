#!/bin/sh
export MYSQL_HOST=localhost
export MYSQL_DATABASE=mysql
export MYSQL_USER=root
export MYSQL_PORT=3306
export MYSQL_ROOT_PASSWORD=mysqlcontainer123

echo Starting MySQL v.5.7.27
docker run --name mysql-database -p 3306:3306 -e MYSQL_ROOT_PASSWORD=$MYSQL_ROOT_PASSWORD -e MYSQL_DATABASE=$MYSQL_DATABASE -d mysql:5.7.27
sleep 60
echo Building supply collector
dotnet restore -s https://www.myget.org/F/s2/ -s https://api.nuget.org/v3/index.json
dotnet build
echo Loading unit test data
docker exec -i mysql-database mysql -u$MYSQL_USER -p$MYSQL_ROOT_PASSWORD $MYSQL_DATABASE < MySqlSupplyCollectorLoader/tests/data.sql
sleep 10
dotnet test
echo Cleanup
docker stop mysql-database
docker rm mysql-database

echo Starting MySQL:latest
docker run --name mysql-database -p 3306:3306 -e MYSQL_ROOT_PASSWORD=$MYSQL_ROOT_PASSWORD -e MYSQL_DATABASE=$MYSQL_DATABASE -d mysql:latest
sleep 60
echo Loading unit test data
docker exec -i mysql-database mysql -u$MYSQL_USER -p$MYSQL_ROOT_PASSWORD $MYSQL_DATABASE < MySqlSupplyCollectorLoader/tests/data.sql
sleep 10
dotnet test
echo Cleanup
docker stop mysql-database
docker rm mysql-database
