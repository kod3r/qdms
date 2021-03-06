﻿// -----------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="">
// Copyright 2013 Alexander Soffronow Pagonidis
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EntityData;
using MahApps.Metro.Controls;
using MySql.Data.MySqlClient;
using NLog;
using NLog.Targets;
using QDMS;
using QDMSServer.DataSources;
using System.Configuration;

namespace QDMSServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private readonly RealTimeDataBroker _realTimeBroker;
        private readonly HistoricalDataBroker _historicalDataBroker;
        private readonly InstrumentsServer _instrumentsServer;

        private readonly QDMSClient.QDMSClient _client;

        private ProgressBar _progressBar;

        public ObservableCollection<Instrument> Instruments { get; set; }
        public ObservableCollection<LogEventInfo> LogMessages { get; set; }
        
        public MainWindow()
        {
            //make sure we can connect to the database
            CheckDBConnection();

            //and that the db we want is there...create it otherwise
            CheckDBExists();

            //set the log directory
            SetLogDirectory();
            
            //set the connection string
            DBUtils.SetConnectionString();

            InitializeComponent();
            DataContext = this;

            LogMessages = new ObservableCollection<LogEventInfo>();

            //target is where the log managers send their logs, here we grab the memory target which has a Subject to observe
            var target = LogManager.Configuration.AllTargets.Single(x => x.Name == "myTarget") as MemoryTarget;

            //we subscribe to the messages and send them all to the LogMessages collection
            if (target != null)
                target.Messages.Subscribe(msg => LogMessages.Add(msg));

            //build the instruments grid context menu
            //we want a button for each BarSize enum value in the UpdateFreqSubMenu menu
            foreach (int value in Enum.GetValues(typeof(BarSize)))
            {
                var button = new MenuItem
                {
                    Header = Regex.Replace(((BarSize) value).ToString(), "([A-Z])", " $1").Trim(),
                    Tag = (BarSize) value
                };
                button.Click += UpdateHistoricalDataBtn_ItemClick;
                ((MenuItem)Resources["UpdateFreqSubMenu"]).Items.Add(button);
            }

            var entityContext = new MyDBContext();
            
            //build the tags menu
            var allTags = entityContext.Tags.ToList();
            BuildTagContextMenu(allTags);

            Instruments = new ObservableCollection<Instrument>();

            var mgr = new InstrumentManager();
            var instrumentList = mgr.FindInstruments(entityContext);

            foreach (Instrument i in instrumentList)
            {
                Instruments.Add(i);
            }
            
            

            _realTimeBroker = new RealTimeDataBroker(Properties.Settings.Default.rtDBPubPort, Properties.Settings.Default.rtDBReqPort);
            _instrumentsServer = new InstrumentsServer(Properties.Settings.Default.instrumentServerPort);
            _historicalDataBroker = new HistoricalDataBroker(Properties.Settings.Default.hDBPort);

            _client = new QDMSClient.QDMSClient(
                "SERVERCLIENT", 
                "localhost",
                Properties.Settings.Default.rtDBReqPort,
                Properties.Settings.Default.rtDBPubPort, 
                Properties.Settings.Default.instrumentServerPort,
                Properties.Settings.Default.hDBPort);
            _client.Connect();
            _client.HistoricalDataReceived += _client_HistoricalDataReceived;


            ActiveStreamGrid.ItemsSource = _realTimeBroker.ActiveStreams; //doesn't really work properly

            entityContext.Dispose();
        }

        //creates a context menu to set tags on instruments
        private void BuildTagContextMenu(IEnumerable<Tag> tags)
        {
            var tagMenu = (MenuItem)Resources["InstrumentTagMenu"];
            tagMenu.Items.Clear();

            foreach (Tag t in tags)
            {
                var button = new MenuItem
                {
                    Header = t.Name,
                    Tag = t.ID,
                    IsCheckable = true,
                    Style = (Style)Resources["TagCheckStyle"]
                };

                button.Click += SetTag_ItemClick;
                tagMenu.Items.Add(button);
            }
            tagMenu.Items.Add(Resources["NewTagMenuItem"]);
        }

        private void SetLogDirectory()
        {
            if (Directory.Exists(Properties.Settings.Default.logDirectory))
            {
                ((FileTarget)LogManager.Configuration.FindTargetByName("default")).FileName = Properties.Settings.Default.logDirectory + "Log.log";
            }
        }

        private void CheckDBConnection()
        {
            //try to establish a database connection. If not possible, prompt the user to enter details
            var connection = DBUtils.CreateConnection(noDB: true);
            try
            {
                connection.Open();
            }
            catch (Exception)
            {
                var dbDetailsWindow = new DBConnectionWindow();
                dbDetailsWindow.ShowDialog();
            }
            connection.Close();
        }

        //check if the database exists, and if not, create it
        private void CheckDBExists()
        {
            if (!DBUtils.CheckDBExists())
            {
                var dialogResult = MessageBox.Show("Database not found, do you want to create it?", "Database Creation", MessageBoxButton.YesNo);
                if (dialogResult == MessageBoxResult.Yes)
                {
                    try
                    {
                        var connection = DBUtils.CreateConnection(noDB: true);
                        connection.Open();
                        var cmd = new MySqlCommand("", connection);

                        cmd.CommandText = DBUtils.GetSQLResource("qdms.sql");
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = DBUtils.GetSQLResource("qdmsdata.sql");
                        cmd.ExecuteNonQuery();
                        connection.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error creating database: " + ex.Message);
                        Close();
                    }
                }
                else
                {
                    Close();
                }
            }
        }


        void _client_HistoricalDataReceived(object sender, HistoricalDataEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
                {
                    _progressBar.Value++;
                    if (_progressBar.Value >= _progressBar.Maximum)
                    {
                        _progressBar.Value = 0;
                        _progressBar.Maximum = 0;
                        StatusBarLabel.Content = "Historical data update complete";
                    }
                    else
                    {
                        StatusBarLabel.Content = string.Format("Rcvd {0} bars of {1} @ {2}",
                            e.Data.Count,
                            e.Request.Instrument.Symbol,
                            e.Request.Frequency);
                    }
                }
                );
        }

        //check the latest date we have available in local storage, then request historical data from that date to the current time
        void UpdateHistoricalDataBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var frequency = (BarSize)((MenuItem)sender).Tag;
            List<Instrument> selectedInstruments = InstrumentsGrid.SelectedItems.Cast<Instrument>().ToList();

            int requestCount = 0;

            using (var localStorage = new MySQLStorage())
            {
                foreach (Instrument i in selectedInstruments)
                {
                    if (!i.ID.HasValue) continue;

                    var storageInfo = localStorage.GetStorageInfo(i.ID.Value);
                    if (storageInfo.Any(x => x.Frequency == frequency))
                    {
                        var relevantStorageInfo = storageInfo.First(x => x.Frequency == frequency);
                        _client.RequestHistoricalData(new HistoricalDataRequest(
                            i,
                            frequency,
                            relevantStorageInfo.LatestDate + frequency.ToTimeSpan(),
                            DateTime.Now,
                            true,
                            false,
                            true));
                        requestCount++;
                    }
                }
            }

            if (_progressBar.Value >= _progressBar.Maximum)
            {
                _progressBar.Maximum = requestCount;
                _progressBar.Value = 0;
            }
            else
            {
                _progressBar.Maximum += requestCount;
            }
        }



        //the application is closing, shut down all the servers and stuff
        private void DXWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _client.Disconnect();
            _client.Dispose();

            _realTimeBroker.StopServer();
            _realTimeBroker.Dispose();

            _historicalDataBroker.StopServer();
            _historicalDataBroker.Dispose();

            _instrumentsServer.StopServer();
            _instrumentsServer.Dispose();
        }

        //exiting the application
        private void BtnExit_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            Close();
        }

        //show the interactive brokers add instrument window
        private void AddInstrumentIBBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var window = new AddInstrumentInteractiveBrokersWindow();

            if (window.AddedInstruments != null)
            {
                foreach (Instrument i in window.AddedInstruments)
                {
                    Instruments.Add(i);
                }
                window.Close();
            }
        }

        //show the Quandl add instrument window
        private void AddInstrumentQuandlBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new AddInstrumentQuandlWindow();

            if (window.AddedInstruments != null)
            {
                foreach (Instrument i in window.AddedInstruments)
                {
                    Instruments.Add(i);
                }
                window.Close();
            }
        }

        //show a window to modify the selected instrument
        private void TableView_RowDoubleClick(object sender, MouseButtonEventArgs mouseButtonEventArgs)
        {
            var inst = (Instrument)InstrumentsGrid.SelectedItem;
            var window = new AddInstrumentManuallyWindow(inst, false);
            window.ShowDialog();

            CollectionViewSource.GetDefaultView(InstrumentsGrid.ItemsSource).Refresh();

            window.Close();
        }

        //show the window to add a new custom futures contract
        private void BtnAddCustomFutures_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var window = new AddInstrumentManuallyWindow(addingContFut: true);
            window.ShowDialog();
            if (window.InstrumentAdded)
            {
                Instruments.Add(window.TheInstrument);
            }
            window.Close();
        }

        private void AddInstrumentManuallyBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var window = new AddInstrumentManuallyWindow();
            window.ShowDialog();
            if (window.InstrumentAdded)
            {
                Instruments.Add(window.TheInstrument);
            }
            window.Close();
        }

        //clone an instrument
        private void InstrumentContextCloneBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var inst = (Instrument)InstrumentsGrid.SelectedItem;
            var window = new AddInstrumentManuallyWindow(inst);
            window.ShowDialog();
            if (window.InstrumentAdded)
            {
                Instruments.Add(window.TheInstrument);
            }
            window.Close();
        }

        //delete one or more instruments
        private void DeleteInstrumentBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            using (var entityContext = new MyDBContext())
            {
                var selectedInstruments = InstrumentsGrid.SelectedItems;
                if (selectedInstruments.Count == 0) return;

                if (selectedInstruments.Count == 1)
                {
                    var inst = (Instrument)selectedInstruments[0];
                    MessageBoxResult res = MessageBox.Show(string.Format("Are you sure you want to delete {0} @ {1}?", inst.Symbol, inst.Datasource.Name),
                        "Delete", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }
                else
                {
                    MessageBoxResult res = MessageBox.Show(string.Format("Are you sure you want to delete {0} instruments?", selectedInstruments.Count),
                        "Delete", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }

                List<Instrument> toRemove = new List<Instrument>();

                foreach (Instrument i in InstrumentsGrid.SelectedItems)
                {
                    entityContext.Instruments.Attach(i);
                    entityContext.Instruments.Remove(i);
                    toRemove.Add(i);
                }

                using (var localStorage = new MySQLStorage())
                {
                    localStorage.Connect();

                    while (toRemove.Count > 0)
                    {
                        Instruments.Remove(toRemove[toRemove.Count - 1]);
                        localStorage.DeleteAllInstrumentData(toRemove[toRemove.Count - 1]);
                        toRemove.RemoveAt(toRemove.Count - 1);
                    }
                }

            
                entityContext.SaveChanges();
            }
        }

        private void EditDataBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var selectedInstruments = InstrumentsGrid.SelectedItems;
            if (selectedInstruments.Count != 1) return;

            var selectedInstrument = (Instrument)selectedInstruments[0];
            var window = new DataEditWindow(selectedInstrument);
            window.ShowDialog();
        }

        private void ImportDataBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var selectedInstruments = InstrumentsGrid.SelectedItems;
            if (selectedInstruments.Count != 1) return;

            var selectedInstrument = (Instrument)selectedInstruments[0];
            var window = new DataImportWindow(selectedInstrument);
            window.ShowDialog();

        }

        private void ExchangesBtn_OnItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var window = new ExchangesWindow();
            window.ShowDialog();
        }

        private void SessionTemplateBtn_OnItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var window = new SessionTemplatesWindow();
            window.ShowDialog();
        }

        private void RootSymbolsBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new RootSymbolsWindow();
            window.ShowDialog();
        }

        private void PBar_Loaded(object sender, RoutedEventArgs e)
        {
            _progressBar = (ProgressBar) sender;
        }
        
        //delete data from selected instruments
        private void ClearDataBtn_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var selectedInstruments = InstrumentsGrid.SelectedItems;
            if (selectedInstruments.Count == 0) return;

            if (selectedInstruments.Count == 1)
            {
                var inst = (Instrument)selectedInstruments[0];
                MessageBoxResult res = MessageBox.Show(string.Format("Are you sure you want to delete all data from {0} @ {1}?", inst.Symbol, inst.Datasource.Name),
                    "Delete", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.No) return;
            }
            else
            {
                MessageBoxResult res = MessageBox.Show(string.Format("Are you sure you want to delete all data from {0} instruments?", selectedInstruments.Count),
                    "Delete", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.No) return;
            }


            using (var storage = new MySQLStorage())
            {
                foreach (Instrument i in selectedInstruments)
                {
                    try
                    {
                        storage.DeleteAllInstrumentData(i);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                }
            }

            StatusBarLabel.Content = "Instrument data deleted";
        }

        //adds or removes a tag from one or more instruments
        private void SetTag_ItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            using (var context = new MyDBContext())
            {
                var selectedInstruments = InstrumentsGrid.SelectedItems;
                var btn = (MenuItem)routedEventArgs.Source;

                //one instrument selected
                foreach (Instrument instrument in selectedInstruments)
                {
                    context.Instruments.Attach(instrument);

                    if (btn.IsChecked)
                    {
                        var tag = context.Tags.First(x => x.ID == (int)btn.Tag);
                        context.Tags.Attach(tag);
                        instrument.Tags.Add(tag);
                    }
                    else
                    {
                        btn.IsChecked = false;
                        var tmpTag = instrument.Tags.First(x => x.ID == (int)btn.Tag);
                        context.Tags.Attach(tmpTag);
                        instrument.Tags.Remove(tmpTag);
                    }
                }

                context.SaveChanges();

                CollectionViewSource.GetDefaultView(InstrumentsGrid.ItemsSource).Refresh();
            }
        }

        private void BtnSettings_OnItemClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        //tag menu is opening, populate it with all tags and set the appropriate checkbox values
        private void InstrumentTagMenu_OnSubmenuOpened(object sender, RoutedEventArgs e)
        {
            List<Instrument> selectedInstruments = InstrumentsGrid.SelectedItems.Cast<Instrument>().ToList();
            if (selectedInstruments.Count == 0)
            {
                return;
            }
            else if (selectedInstruments.Count == 1)
            {
                var instrument = (Instrument)InstrumentsGrid.SelectedItem;
                //set checkboxes on the selected tags
                var instrumentTagMenu = (MenuItem)Resources["InstrumentTagMenu"];
                foreach (MenuItem btn in instrumentTagMenu.Items)
                {
                    if (btn.Tag == null) continue;

                    btn.IsChecked = instrument.Tags.Any(x => x.ID == (int)btn.Tag);
                    btn.IsEnabled = true;
                }
            }
            else
            {
                var instrumentTagMenu = (MenuItem)Resources["InstrumentTagMenu"];
                foreach (MenuItem btn in instrumentTagMenu.Items)
                {
                    if (btn.Tag == null) continue;

                    int tagCount = selectedInstruments.Count(x => x.Tags != null && x.Tags.Any(y => y.ID == (int)btn.Tag));
                    if (tagCount == 0 || tagCount == selectedInstruments.Count)
                    {
                        btn.IsEnabled = true;
                        btn.IsChecked = tagCount == selectedInstruments.Count;
                    }
                    else //if tags have different values among the selected instruments, just disable the button
                    {
                        btn.IsEnabled = false;
                    }
                }
            }
        }

        //add a new tag from the context menu and then apply it to the selected instruments
        private void NewTagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            using (var context = new MyDBContext())
            {
                string newTagName = ((TextBox)sender).Text;
                if (context.Tags.Any(x => x.Name == newTagName)) return; //tag already exists

                //add the tag
                var newTag = new Tag { Name = newTagName };
                context.Tags.Add(newTag);


                //apply the tag to the selected instruments
                var selectedInstruments = InstrumentsGrid.SelectedItems.Cast<Instrument>();
                foreach (Instrument i in selectedInstruments)
                {
                    context.Instruments.Attach(i);
                    i.Tags.Add(newTag);
                }

                //update the tag menu
                var allTags = context.Tags.ToList();
                BuildTagContextMenu(allTags);

                context.SaveChanges();
            }

            CollectionViewSource.GetDefaultView(InstrumentsGrid.ItemsSource).Refresh();
        }

        private void NewDataRequestBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new HistoricalRequestWindow((Instrument)InstrumentsGrid.SelectedItem);
        }

        //enable/disable menuitems in the row context menu depending on what has been selected
        private void ContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            //horrible, but what can you do?
            bool multipleSelected = InstrumentsGrid.SelectedItems.Count > 1;
            ContextMenu menu = (ContextMenu)Resources["RowMenu"];

            ((MenuItem)menu.Items[0]).IsEnabled = !multipleSelected; //new data request
            ((MenuItem)menu.Items[4]).IsEnabled = !multipleSelected; //clone
            ((MenuItem)menu.Items[5]).IsEnabled = !multipleSelected; //import data
            ((MenuItem)menu.Items[6]).IsEnabled = !multipleSelected; //edit data
        }



    }
}
