# Ejecutar en la raiz de la solución
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish
scp -r .\publish root@datalake1.linplatform.com:/var/www/demon