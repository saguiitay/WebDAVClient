<?php
require '/var/www/vendor/autoload.php';

$server = new Sabre\DAV\Server(new Sabre\DAV\FS\Directory('/var/www/data/files'));
$server->setBaseUri('/');
$backend = new Sabre\DAV\Auth\Backend\BasicCallBack(function ($user, $password) {
    return hash_equals(getenv('DAV_USER'), $user) && hash_equals(getenv('DAV_PASSWORD'), $password);
});
$server->addPlugin(new Sabre\DAV\Auth\Plugin($backend, 'Isolated interoperability fixture'));
$server->addPlugin(new Sabre\DAV\Locks\Plugin(new Sabre\DAV\Locks\Backend\File('/var/www/data/locks')));
$pdo = new PDO('sqlite:/var/www/data/properties.sqlite');
$pdo->exec('CREATE TABLE IF NOT EXISTS propertystorage (id INTEGER PRIMARY KEY AUTOINCREMENT, path VARBINARY(1024), name VARBINARY(100), valuetype INTEGER, value BLOB)');
$server->addPlugin(new Sabre\DAV\PropertyStorage\Plugin(new Sabre\DAV\PropertyStorage\Backend\PDO($pdo)));
$server->addPlugin(new Sabre\DAV\Sync\Plugin());
$server->exec();
