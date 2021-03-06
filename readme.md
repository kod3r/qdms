The QUSMA Data Management System (QDMS) is an application for acquiring, managing, and distributing low-frequency historical and real-time data, written in C#. 

QDMS uses a client/server model. The server acts as a broker between clients and external data sources. It also manages metadata on instruments, and local storage of historical data. Finally it also functions as a UI for managing the metadata & data, as well as importing/exporting data from and to CSV files. [Here's a rough view of how the systems are connected to each other](http://i.imgur.com/qUWlpj7.png).

A simple sample application showing usage of the client can be found in the SampleApp project.

QDMS uses MySQL for storage, ZeroMQ and Protocol Buffers for client/server communications, MahApps.Metro for the interface, and ib-csharp to communicate with IB's TWS.

If you wish to contribute, fork the repo and send a pull request with your changes.

For bug reports, feature requests, and general discussion please use the [google group](https://groups.google.com/forum/#!forum/qusma-data-management-system).

Screenshots:
------------------------
* [Instrument metadata](http://i.imgur.com/GXw8amN.png).
* [The main server interface](http://i.imgur.com/i985ZUW.png).
* [Adding a new instrument from IB](http://i.imgur.com/HGPsoK5.png).
* [Importing CSV data](http://i.imgur.com/en6kDo1.png).
* [Editing futures expiration rules](http://i.imgur.com/WvKkb4x.png).
* [Continuous futures options](http://i.imgur.com/47VuXmH.png).

Currently Supported Data Sources:
------------------------
* Yahoo
* Interactive Brokers
* Quandl

Requirements:
------------------------
* A reasonably recent version of MySQL.
* .NET 4.5

Planned features/improvements:
------------------------
* Continuous futures.
* Constructing low-frequency bars from higher frequency data.
* Support for more data sources.
* Support for fundamental data.
* Alternative (binary files) storage mechanism for tick data.
* Some sort of market-wide "snapshot" functionality.
* Far wider test coverage.
* Proper docs.