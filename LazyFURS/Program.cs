﻿using LazyFURS.Models;
using LazyFURS.Models.Xlsx;
using LazyFURS.Models.Xml.SpecialTypes;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LazyFURS
{
    internal class Program
    {
        private static readonly HttpClient client = new();

        private static Conversion[] conversionData;
        private static List<XlsxDividend> dividends;
        private static List<XlsxPosition> positions;

        private static string exportName;

        private static CultureInfo xlsxNumbersCulture;
        private static CultureInfo xmlDatesCulture;

        private static bool compactDividend;
        private static bool compactPositions;

        private static IsinToAddress isinToAddress;
        private static IsinToCountry isinToCountry;
        private static CountryToTaxReduction countryToTaxReduction;

        private static async Task Main()
        {
            WelcomeMessage();

            WarningMessage();

            xlsxNumbersCulture = new CultureInfo("de-DE");
            xmlDatesCulture = new CultureInfo("fr-FR");

            //Prepares the export, example: Etoro_EUR_report_15.1.2022.xlsx
            exportName = "Etoro_EUR_report_" + DateTime.Now.Day + "_" + DateTime.Now.Month + "_" + DateTime.Now.Year + ".xlsx";

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            dividends = new List<XlsxDividend>();
            positions = new List<XlsxPosition>();

            Console.WriteLine("This location will be used as the path for the exports as well.");
            Console.Write("Path to your Etoro report: ");
            string filePath = Console.ReadLine();

            Console.WriteLine();
            Console.WriteLine();
            Console.Write("Would you like to combine dividends of the same source + same date into a single entity (needed if you have multiple dividends from the same source on the same day)? (y/N)? ");
            char input = Console.ReadKey().KeyChar;
            compactDividend = input == 'y' || input == 'Y';
            Console.WriteLine();
            Console.WriteLine();
            Console.Write("Would you like to combine positions of the same source + same date + same price into a single entity? (y/N)? ");
            input = Console.ReadKey().KeyChar;
            compactPositions = input == 'y' || input == 'Y';

            isinToAddress = new();
            isinToCountry = new();
            countryToTaxReduction = new();
            FileInfo existingFile = null;

            await ReadCurrenciesApiData();

            GenerateOptionsMenu();
            input = Console.ReadKey().KeyChar;

            if (input != '1')
            {
                //Prepare the data for export
                existingFile = new(filePath);
                using ExcelPackage package = new(existingFile); //the using statement automatically calls Dispose() which closes the package.
                ReadClosedPosition(package);
                ReadDividends(package);

                if (compactDividend && dividends.Count > 1)
                {
                    CompactDividends();
                }

                if (compactPositions && positions.Count > 1)
                {
                    CompactPositions();
                }
            }

            while (input != '1')
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();

                if (input == '2')
                {
                    ExportToXlsxFile(existingFile.DirectoryName);
                }
                else if (input == '3')
                {
                    PrepareDivReport(existingFile.DirectoryName);
                }
                else if (input == '4')
                {
                    PrepareKdvpReport(existingFile.DirectoryName);
                }
                else if (input == '5')
                {
                    PrepareDIfiReport(existingFile.DirectoryName);
                }
                GenerateOptionsMenu();
                input = Console.ReadKey().KeyChar;
            }

            Console.WriteLine("");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        private static void WriteExportDone()
        {
            Console.WriteLine();
            Console.WriteLine("Export done!");
            Console.WriteLine();
        }

        private static void WarningMessage()
        {
            Console.WriteLine();
            Console.WriteLine("THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.");
            Console.WriteLine("YOU BAER ALL RESPONSIBILITY FOR ANY PROBLEMS WHEN SUBMITTING YOUR TAX REPORTS.");
            Console.WriteLine();
        }

        private static void WelcomeMessage()
        {
            Console.WriteLine(" __________________________ ");
            Console.WriteLine("|  ______________________  |");
            Console.WriteLine("| | Welcome to LazyFURS! | |");
            Console.WriteLine("| |______________________| |");
            Console.WriteLine("|__________________________|");
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void ExportToXlsxFile(string folderPath)
        {
            using ExcelPackage newPackage = new();
            // Prepare dividends worksheet
            PrepareDividends(newPackage);

            // Prepare closed positions sheet
            PrepareClosedPosition(newPackage);

            // Set some document properties
            newPackage.Workbook.Properties.Title = "Etoro EUR statement";

            // Save our new workbook in the output directory and we are done!
            newPackage.SaveAs(folderPath + "\\" + exportName);

            WriteExportDone();
        }

        private static void PrepareDividends(ExcelPackage newPackage)
        {
            //Add a new worksheet to the empty workbook
            ExcelWorksheet dividendsSheet = newPackage.Workbook.Worksheets.Add("Dividends_EUR");

            //Add the headers
            dividendsSheet.Cells[1, 1].Value = "Payment Date";
            dividendsSheet.Column(1).Width = 15;
            dividendsSheet.Cells[1, 2].Value = "ISIN";
            dividendsSheet.Column(2).Width = 20;
            dividendsSheet.Cells[1, 3].Value = "Full Name";
            dividendsSheet.Column(3).Width = 40;
            dividendsSheet.Cells[1, 4].Value = "EUR NET Dividend";
            dividendsSheet.Column(4).Width = 20;
            dividendsSheet.Cells[1, 5].Value = "EUR Foreign Tax";
            dividendsSheet.Column(5).Width = 20;

            for (int i = 0; i < dividends.Count; i++)
            {
                //Add data
                dividendsSheet.Cells[i + 2, 1].Value = dividends[i].PaymentDate.ToShortDateString();
                dividendsSheet.Cells[i + 2, 2].Value = dividends[i].ISIN;
                dividendsSheet.Cells[i + 2, 3].Value = dividends[i].FullName;
                dividendsSheet.Cells[i + 2, 4].Value = Math.Round(dividends[i].EuroNetDividend, 2).ToString(xlsxNumbersCulture);
                dividendsSheet.Cells[i + 2, 5].Value = Math.Round(dividends[i].EuroForeignTax, 2).ToString(xlsxNumbersCulture);
            }
        }

        private static void PrepareDivReport(string directoryName)
        {
            uint vatId = SpecifyVatId();
            bool lowerTax = LowerTaxConfirmation();
            Models.Xml.Div.Envelope envelope = new()
            {
                Header = new()
                {
                    domain = "edavki.durs.si",
                    CustodianInfo = new(),
                    responseTo = null,
                    taxpayer = new()
                    {
                        taxNumber = vatId,
                        taxpayerType = nameof(TaxPayerType.FO),
                    },
                    Workflow = new()
                    {
                        DocumentWorkflowID = "O",
                        DocumentWorkflowName = null
                    },
                },
                AttachmentList = Array.Empty<Models.Xml.Div.AttachmentListExternalAttachment>(),
                Signatures = new(),
                body = new()
                {
                    Doh_Div = new()
                    {
                        Period = (DateTime.UtcNow.Year - 1).ToString(),
                    },
                    Dividend = new Models.Xml.Div.EnvelopeBodyDividend[dividends.Count]
                }
            };
            for (int i = 0; i < dividends.Count; i++)
            {
                string country = isinToCountry.GetCountry(dividends[i].ISIN);
                envelope.body.Dividend[i] = new Models.Xml.Div.EnvelopeBodyDividend
                {
                    Date = dividends[i].PaymentDate,
                    PayerIdentificationNumber = dividends[i].ISIN,
                    PayerName = dividends[i].FullName,
                    PayerAddress = isinToAddress.GetAddress(dividends[i].ISIN),
                    PayerCountry = country,
                    Type = "1",
                    Value = Math.Round(dividends[i].EuroNetDividend + dividends[i].EuroForeignTax, 2),
                    ForeignTax = Math.Round(dividends[i].EuroForeignTax, 2),
                    SourceCountry = country,
                };
                if (lowerTax)
                {
                    envelope.body.Dividend[i].ReliefStatement = countryToTaxReduction.GetDoubleTaxationExemption(country);
                }
            }

            System.Xml.Serialization.XmlSerializer writer = new(typeof(Models.Xml.Div.Envelope));

            string path = directoryName + "//Doh_Div.xml";
            FileStream file = File.Create(path);

            writer.Serialize(file, envelope);
            file.Close();

            WriteExportDone();
        }

        private static bool LowerTaxConfirmation()
        {
            Console.WriteLine("Would you like your tax reduced using international treatise if they exist? (y/N):");
            char input = Console.ReadKey().KeyChar;
            return input == 'y' || input == 'Y';
        }

        private static uint SpecifyVatId()
        {
            Console.WriteLine("Specify your VAT ID: ");
            uint vatId;
            while (!uint.TryParse(Console.ReadLine(), out vatId))
            {
                Console.WriteLine("Specify your VAT ID: ");
            }

            return vatId;
        }

        private static void PrepareKdvpReport(string directoryName)
        {
            List<XlsxPosition> nonCfdPositions = new(positions.Count);
            foreach (XlsxPosition position in positions)
            {
                if (position.Type != "CFD")
                {
                    if (position.Type == "Crypto")
                    {
                        continue;
                    }
                    nonCfdPositions.Add(position);
                }
            }

            uint vatId = SpecifyVatId();

            Models.Xml.KDVP.Envelope envelope = new()
            {
                Header = new()
                {
                    taxpayer = new()
                    {
                        taxNumber = vatId,
                        taxpayerType = nameof(TaxPayerType.FO),
                    },
                    domain = "edavki.durs.si"
                },
                AttachmentList = Array.Empty<Models.Xml.KDVP.AttachmentListExternalAttachment>(),
                Signatures = new Models.Xml.KDVP.Signatures(),
                body = new Models.Xml.KDVP.EnvelopeBody()
                {
                    bodyContent = string.Empty,
                    Doh_KDVP = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVP()
                    {
                        KDVP = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVP()
                        {
                            DocumentWorkflowID = "O",
                            Year = DateTime.UtcNow.Year - 1,
                            PeriodStart = new DateTime(DateTime.Now.Year - 1, 1, 1),
                            PeriodEnd = new DateTime(DateTime.Now.Year - 1, 12, 31),
                        },
                        KDVPItem = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItem[nonCfdPositions.Count],
                    }
                }
            };

            List<XlsxPosition> samePositions = new(nonCfdPositions.Count);
            int KDVPItemIndex = 0;
            for (int i = 0; i < nonCfdPositions.Count; i++)
            {
                samePositions.Add(nonCfdPositions[i]);
                Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecurities securities = new()
                {
                    IsFond = false,
                    ISIN = nonCfdPositions[i].ISIN
                };

                for (int j = i + 1; j < nonCfdPositions.Count; j++)
                {
                    if (nonCfdPositions[i].ISIN == nonCfdPositions[j].ISIN)
                        samePositions.Add(nonCfdPositions[j]);
                    else
                        break;
                }

                securities.Row = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecuritiesRow[samePositions.Count * 2];
                for (int j = 0; j < samePositions.Count; j++)
                {
                    securities.Row[j * 2] = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecuritiesRow
                    {
                        ID = j * 2,
                        F8 = samePositions[j].Units,

                        Purchase = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecuritiesRowPurchase
                        {
                            F1 = samePositions[j].OpenDate,
                            F2 = "B",
                            F3 = samePositions[j].Units,
                            F4 = samePositions[j].EuroOpenPrice
                        }
                    };
                    securities.Row[j * 2 + 1] = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecuritiesRow
                    {
                        ID = j * 2 + 1,
                        F8 = 0,

                        Sale = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItemSecuritiesRowSale
                        {
                            F6 = samePositions[j].CloseDate,
                            F7 = samePositions[j].Units,
                            F9 = samePositions[j].EuroClosePrice,
                            F10 = false
                        }
                    };
                }

                envelope.body.Doh_KDVP.KDVPItem[KDVPItemIndex] = new Models.Xml.KDVP.EnvelopeBodyDoh_KDVPKDVPItem
                {
                    ItemID = KDVPItemIndex + 1,
                    InventoryListType = "PLVP",
                    HasForeignTax = false,
                    HasLossTransfer = false,
                    ForeignTransfer = false,
                    TaxDecreaseConformance = false,
                    Name = nonCfdPositions[i].ISIN,
                    Securities = securities,
                };
                i += samePositions.Count - 1;
                samePositions.Clear();
                KDVPItemIndex++;
            }

            System.Xml.Serialization.XmlSerializer writer = new(typeof(Models.Xml.KDVP.Envelope));

            string path = directoryName + "//Doh_KDVP.xml";
            FileStream file = File.Create(path);

            writer.Serialize(file, envelope);
            file.Close();

            WriteExportDone();
        }

        private static void PrepareDIfiReport(string directoryName)
        {
            List<XlsxPosition> cfdPositions = new(positions.Count);
            foreach (XlsxPosition position in positions)
            {
                if (position.Type == "CFD")
                    cfdPositions.Add(position);
            }

            uint vatId = SpecifyVatId();

            Models.Xml.IFI.Envelope envelope = new()
            {
                Header = new Models.Xml.IFI.Header
                {
                    taxpayer = new Models.Xml.IFI.HeaderTaxpayer
                    {
                        taxNumber = vatId,
                        taxpayerType = nameof(TaxPayerType.FO),
                    },
                    Workflow = new Models.Xml.IFI.HeaderWorkflow
                    {
                        DocumentWorkflowID = "O"
                    },
                    domain = "edavki.durs.si",
                },
                AttachmentList = Array.Empty<Models.Xml.IFI.AttachmentListExternalAttachment>(),
                Signatures = new Models.Xml.IFI.Signatures(),
                body = new Models.Xml.IFI.EnvelopeBody
                {
                    bodyContent = string.Empty,
                    D_IFI = new Models.Xml.IFI.EnvelopeBodyD_IFI
                    {
                        PeriodStart = new DateTime(DateTime.Now.Year - 1, 1, 1),
                        PeriodEnd = new DateTime(DateTime.Now.Year - 1, 12, 31),
                        TItem = new Models.Xml.IFI.EnvelopeBodyD_IFITItem[cfdPositions.Count]
                    }
                }
            };

            List<XlsxPosition> sameLongPosition = new(cfdPositions.Count);
            List<XlsxPosition> sameShortPosition = new(cfdPositions.Count);
            int TItemIndex = 0;
            for (int i = 0; i < cfdPositions.Count; i++)
            {
                if (cfdPositions[i].IsLong)
                    sameLongPosition.Add(cfdPositions[i]);
                else
                    sameShortPosition.Add(cfdPositions[i]);

                for (int j = i + 1; j < cfdPositions.Count; j++)
                {
                    if (cfdPositions[j].FullName == cfdPositions[i].FullName && cfdPositions[j].IsLong)
                        sameLongPosition.Add(cfdPositions[j]);
                    else if (cfdPositions[i].FullName == cfdPositions[j].FullName && !cfdPositions[j].IsLong)
                        sameShortPosition.Add(cfdPositions[j]);
                    else
                        break;
                }

                if (sameLongPosition.Count > 0)
                {
                    envelope.body.D_IFI.TItem[TItemIndex] = new Models.Xml.IFI.EnvelopeBodyD_IFITItem
                    {
                        TypeId = "PLIFI",
                        Type = "02",
                        TypeName = "finančne pogodbe na razliko",
                        Name = cfdPositions[i].FullName.Trim(),
                        HasForeignTax = false,
                    };
                    Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItem[] TSubItem = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItem[sameLongPosition.Count * 2];
                    for (int j = 0; j < sameLongPosition.Count; j++)
                    {
                        TSubItem[j * 2] = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItem
                        {
                            F8 = sameLongPosition[j].Units,
                            Purchase = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItemPurchase
                            {
                                F1 = sameLongPosition[j].OpenDate,
                                F2 = "A",
                                F3 = sameLongPosition[j].Units,
                                F4 = sameLongPosition[j].EuroOpenPrice,
                                F9 = sameLongPosition[j].Leverage != 1
                            }
                        };
                        TSubItem[j * 2 + 1] = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItem
                        {
                            F8 = 0,
                            Sale = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTSubItemSale
                            {
                                F5 = sameLongPosition[j].CloseDate,
                                F6 = sameLongPosition[j].Units,
                                F7 = sameLongPosition[j].EuroClosePrice,
                            }
                        };
                    }
                    envelope.body.D_IFI.TItem[TItemIndex].TSubItem = TSubItem;
                    TItemIndex++;
                }

                if (sameShortPosition.Count > 0)
                {
                    envelope.body.D_IFI.TItem[TItemIndex] = new Models.Xml.IFI.EnvelopeBodyD_IFITItem
                    {
                        TypeId = "PLIFIShort",
                        Type = "02",
                        TypeName = "finančne pogodbe na razliko",
                        Name = cfdPositions[i].FullName.Trim(),
                        HasForeignTax = false,
                    };

                    Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItem[] TShortSubItem = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItem[sameShortPosition.Count * 2];
                    for (int j = 0; j < sameShortPosition.Count; j++)
                    {
                        TShortSubItem[j * 2] = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItem
                        {
                            F8 = -sameShortPosition[j].Units,
                            Sale = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItemSale
                            {
                                F1 = sameShortPosition[j].OpenDate,
                                F2 = sameShortPosition[j].Units,
                                F3 = sameShortPosition[j].EuroOpenPrice,
                                F9 = sameShortPosition[j].Leverage != 1
                            }
                        };
                        TShortSubItem[j * 2 + 1] = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItem
                        {
                            F8 = 0,
                            Purchase = new Models.Xml.IFI.EnvelopeBodyD_IFITItemTShortSubItemPurchase
                            {
                                F4 = sameShortPosition[j].CloseDate,
                                F5 = "A",
                                F6 = sameShortPosition[j].Units,
                                F7 = sameShortPosition[j].EuroClosePrice,
                            }
                        };
                    }
                    envelope.body.D_IFI.TItem[TItemIndex].TShortSubItem = TShortSubItem;
                    TItemIndex++;
                }
                i += sameLongPosition.Count + sameShortPosition.Count - 1;
                sameLongPosition.Clear();
                sameShortPosition.Clear();
            }

            System.Xml.Serialization.XmlSerializer writer = new(typeof(Models.Xml.IFI.Envelope));

            string path = directoryName + "//D-IFI.xml";
            FileStream file = File.Create(path);

            writer.Serialize(file, envelope);
            file.Close();

            WriteExportDone();
        }

        private static void CompactDividends()
        {
            for (int i = 0; i < dividends.Count; i++)
            {
                if (i > 0 && dividends[i].FullName == dividends[i - 1].FullName && dividends[i].PaymentDate == dividends[i - 1].PaymentDate)
                {
                    dividends[i - 1].EuroNetDividend += dividends[i].EuroNetDividend;
                    dividends[i - 1].EuroForeignTax += dividends[i].EuroForeignTax;
                    dividends.Remove(dividends[i]);
                    i--;
                }
            }
        }

        private static void CompactPositions()
        {
            for (int i = 1; i < positions.Count; i++)
            {
                if (positions[i].FullName == positions[i - 1].FullName
                    && positions[i].OpenDate.Date == positions[i - 1].OpenDate.Date
                    && positions[i].CloseDate.Date == positions[i - 1].CloseDate.Date
                    && positions[i].OpenRate == positions[i - 1].OpenRate
                    && positions[i].CloseRate == positions[i - 1].CloseRate
                    && positions[i].Type == positions[i - 1].Type
                    && positions[i].Leverage == positions[i - 1].Leverage
                    && positions[i].IsLong == positions[i - 1].IsLong)
                {
                    positions[i - 1].EuroStartValue += positions[i].EuroStartValue;
                    positions[i - 1].EuroCloseValue += positions[i].EuroCloseValue;
                    positions[i - 1].EuroProfit += positions[i].EuroProfit;
                    positions[i - 1].Units += positions[i].Units;
                    positions.Remove(positions[i]);
                    i--;
                }
            }
        }

        private static void GenerateOptionsMenu()
        {
            Console.WriteLine();
            Console.WriteLine("What report would you like to generate?");
            Console.WriteLine();
            Console.WriteLine("1) Exit");
            Console.WriteLine("2) Human readable .xlsx report");
            Console.WriteLine("3) Doh_Div report");
            Console.WriteLine("4) Doh_KDVP report");
            Console.WriteLine("5) D-IFI report");
        }

        private static void PrepareClosedPosition(ExcelPackage newPackage)
        {
            //Add a new worksheet to the workbook
            ExcelWorksheet closedPositionSheed = newPackage.Workbook.Worksheets.Add("Closed_positions_EUR");

            //Add the headers
            closedPositionSheed.Cells[1, 1].Value = "ISIN";
            closedPositionSheed.Column(1).Width = 15;
            closedPositionSheed.Cells[1, 2].Value = "Full Name";
            closedPositionSheed.Column(2).Width = 40;
            closedPositionSheed.Cells[1, 3].Value = "Type";
            closedPositionSheed.Column(3).Width = 5;
            closedPositionSheed.Cells[1, 4].Value = "Trade Type";
            closedPositionSheed.Column(4).Width = 10;
            closedPositionSheed.Cells[1, 5].Value = "Units";
            closedPositionSheed.Column(5).Width = 15;
            closedPositionSheed.Cells[1, 6].Value = "Leverage";
            closedPositionSheed.Column(6).Width = 10;
            closedPositionSheed.Cells[1, 7].Value = "Open Date";
            closedPositionSheed.Column(7).Width = 15;
            closedPositionSheed.Cells[1, 8].Value = "Close Date";
            closedPositionSheed.Column(8).Width = 15;
            closedPositionSheed.Cells[1, 9].Value = "EUR Start Value";
            closedPositionSheed.Column(9).Width = 15;
            closedPositionSheed.Cells[1, 10].Value = "EUR Close Value";
            closedPositionSheed.Column(10).Width = 15;
            closedPositionSheed.Cells[1, 11].Value = "EUR Open Price";
            closedPositionSheed.Column(11).Width = 15;
            closedPositionSheed.Cells[1, 12].Value = "EUR Close Price";
            closedPositionSheed.Column(12).Width = 15;
            closedPositionSheed.Cells[1, 13].Value = "EUR Profit";
            closedPositionSheed.Column(13).Width = 15;

            for (int i = 0; i < positions.Count; i++)
            {
                //Add data
                closedPositionSheed.Cells[i + 2, 1].Value = positions[i].ISIN;
                closedPositionSheed.Cells[i + 2, 2].Value = positions[i].FullName;
                closedPositionSheed.Cells[i + 2, 3].Value = positions[i].Type;
                closedPositionSheed.Cells[i + 2, 4].Value = positions[i].IsLong ? "Long" : "Short";
                closedPositionSheed.Cells[i + 2, 5].Value = positions[i].Units.ToString(xlsxNumbersCulture);
                closedPositionSheed.Cells[i + 2, 6].Value = positions[i].Leverage;
                closedPositionSheed.Cells[i + 2, 7].Value = positions[i].OpenDate.ToShortDateString();
                closedPositionSheed.Cells[i + 2, 8].Value = positions[i].CloseDate.ToShortDateString();
                closedPositionSheed.Cells[i + 2, 9].Value = Math.Round(positions[i].EuroStartValue, 2).ToString(xlsxNumbersCulture);
                closedPositionSheed.Cells[i + 2, 10].Value = Math.Round(positions[i].EuroCloseValue, 2).ToString(xlsxNumbersCulture);
                closedPositionSheed.Cells[i + 2, 11].Value = Math.Round(positions[i].EuroOpenPrice, 2).ToString(xlsxNumbersCulture);
                closedPositionSheed.Cells[i + 2, 12].Value = Math.Round(positions[i].EuroClosePrice, 2).ToString(xlsxNumbersCulture);
                closedPositionSheed.Cells[i + 2, 13].Value = Math.Round(positions[i].EuroProfit, 2).ToString(xlsxNumbersCulture);
            }
        }

        private static async Task ReadCurrenciesApiData()
        {
            HttpResponseMessage response = await client.GetAsync("https://sdw-wsrest.ecb.europa.eu/service/data/EXR/D.USD.EUR.SP00.A?format=csvdata");
            if (!response.IsSuccessStatusCode)
            {
                throw new ApplicationException("Data was not retrieved successfully!");
            }

            string allData = await response.Content.ReadAsStringAsync();
            string[] rowData = allData.Split("\n");

            conversionData = new Conversion[rowData.Length - 2];

            for (int i = 1; i < rowData.Length - 1; i++)
            {
                string[] row = rowData[i].Split(',');

                // Date and rate are at 6th and 7th position
                conversionData[i - 1] = new Conversion(row[6], row[7]);
            }

            //This is done to optimize data retrieval
            conversionData = conversionData.OrderByDescending(x => x.IssuingDate).ToArray();
        }

        private static void ReadClosedPosition(ExcelPackage package)
        {
            ExcelWorksheet closedPositionsSheet = package.Workbook.Worksheets[1];
            int index = 2;// Indexing starts at 1... + we skip the header
            bool isLastRow = false;

            while (!isLastRow)
            {
                if (closedPositionsSheet.Cells[index, 1].Value != null)
                {
                    string[] actionSplit = closedPositionsSheet.Cells[index, 2].Value.ToString().Split(' ');
                    XlsxPosition calculatePosition = new()
                    {
                        IsLong = actionSplit[0] == "Buy",
                        FullName = GenerateName(actionSplit),
                        OpenDate = DateTime.ParseExact(closedPositionsSheet.Cells[index, 5].Value.ToString(), "dd/MM/yyyy HH:mm:ss", xmlDatesCulture).Date,
                        CloseDate = DateTime.ParseExact(closedPositionsSheet.Cells[index, 6].Value.ToString(), "dd/MM/yyyy HH:mm:ss", xmlDatesCulture).Date,
                        Leverage = int.Parse(closedPositionsSheet.Cells[index, 7].Value.ToString()),
                        Units = decimal.Parse(closedPositionsSheet.Cells[index, 4].Value.ToString(), NumberStyles.Number, new CultureInfo("en-GB")),
                        Type = closedPositionsSheet.Cells[index, 16].Value.ToString(),
                        ISIN = closedPositionsSheet.Cells[index, 17].Value?.ToString() ?? "",
                    };

                    decimal openRate = GetFirstPossibleRate(calculatePosition.OpenDate).Rate; // optimize rate retrieval
                    decimal closeRate = GetFirstPossibleRate(calculatePosition.CloseDate).Rate; // optimize rate retrieval

                    // Calculate start (open) values
                    calculatePosition.EuroStartValue = decimal.Parse(closedPositionsSheet.Cells[index, 3].Value.ToString()) / openRate;
                    calculatePosition.EuroOpenPrice = calculatePosition.EuroStartValue / calculatePosition.Units / openRate;

                    // Calculate end (close) values
                    calculatePosition.EuroProfit = decimal.Parse(closedPositionsSheet.Cells[index, 9].Value.ToString()) / closeRate;
                    if (calculatePosition.IsLong)
                    {
                        calculatePosition.EuroCloseValue = calculatePosition.EuroStartValue + calculatePosition.EuroProfit;
                        calculatePosition.EuroClosePrice = calculatePosition.EuroCloseValue / calculatePosition.Units / closeRate;
                    }
                    else
                    {
                        calculatePosition.EuroCloseValue = calculatePosition.EuroStartValue + calculatePosition.EuroProfit * -1;
                        calculatePosition.EuroClosePrice = calculatePosition.EuroCloseValue / calculatePosition.Units / closeRate;
                    }

                    // Values in native currency
                    calculatePosition.OpenRate = decimal.Parse(closedPositionsSheet.Cells[index, 10].Value.ToString());
                    calculatePosition.CloseRate = decimal.Parse(closedPositionsSheet.Cells[index, 11].Value.ToString());

                    positions.Add(calculatePosition);

                    index++;
                    continue;
                }
                isLastRow = true;
            }

            //Order position data
            positions = positions.OrderBy(x => x.FullName).ThenBy(x => x.OpenDate).ToList();
        }

        private static string GenerateName(string[] actionSplit)
        {
            string result = "";
            for (int i = 1; i < actionSplit.Length; i++)
            {
                result += actionSplit[i] + " ";
            }
            return result;
        }

        private static void ReadDividends(ExcelPackage package)
        {
            ExcelWorksheet dividendSheet = package.Workbook.Worksheets[3];

            int index = 2;// Indexing starts at 1... + we skip the header
            bool isLastRow = false;

            while (!isLastRow)
            {
                if (dividendSheet.Cells[index, 1].Value != null)
                {
                    XlsxDividend calculateDividend = new()
                    {
                        PaymentDate = DateTime.ParseExact(dividendSheet.Cells[index, 1].Value.ToString(), "dd/MM/yyyy", xmlDatesCulture).Date,
                        FullName = dividendSheet.Cells[index, 2].Value.ToString(),
                        ISIN = dividendSheet.Cells[index, 8].Value.ToString()
                    };

                    decimal rate = GetFirstPossibleRate(calculateDividend.PaymentDate).Rate; // optimize rate retrieval

                    calculateDividend.EuroNetDividend = decimal.Parse(dividendSheet.Cells[index, 3].Value.ToString()) / rate;
                    calculateDividend.EuroForeignTax = decimal.Parse(dividendSheet.Cells[index, 5].Value.ToString()) / rate;

                    dividends.Add(calculateDividend);

                    index++;
                    continue;
                }
                isLastRow = true;
            }

            //Order dividend data
            dividends = dividends.OrderBy(x => x.FullName).ThenBy(x => x.PaymentDate).ToList();
        }

        private static Conversion GetFirstPossibleRate(DateTime d)
        {
            DateTime date = d;
            Conversion result = null;
            while (result == null)
            {
                //Gets the value on the specified date. If the value doesn't exist, it tries to get values from a previous day.
                result = conversionData.FirstOrDefault(x => x.IssuingDate == date);
                date = date.AddDays(-1);
            }
            return result;
        }
    }
}