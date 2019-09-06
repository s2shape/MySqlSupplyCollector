#!/bin/sh
export MYSQL_HOST=localhost
export MYSQL_DATABASE=mysql
export MYSQL_USER=root
export MYSQL_PORT=3306
export MYSQL_ROOT_PASSWORD=mysqlcontainer123

docker run --name mysql-database -p 3306:3306 -e MYSQL_ROOT_PASSWORD=$MYSQL_ROOT_PASSWORD -e MYSQL_DATABASE=$MYSQL_DATABASE -d mysql:latest
sleep 20
docker exec -i mysql-database mysql -u$MYSQL_USER -p$MYSQL_ROOT_PASSWORD $MYSQL_DATABASE < MySqlSupplyCollectorTests/tests/data.sql
sleep 20
dotnet test
docker stop mysql-database
docker rm mysql-database
