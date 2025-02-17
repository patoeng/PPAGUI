﻿using ComponentFactory.Krypton.Toolkit;
using MesData;
using MesData.Login;
using MesData.Ppa;
using OpcenterWikLibrary;
using PPAGUI.Enumeration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Camstar.WCF.ObjectStack;
using MesData.Repair;
using MesData.UnitCounter;
using PPAGUI.Hardware;
using PPAGUI.Properties;
using Environment = System.Environment;
using System.Linq.Dynamic;
using System.Text.RegularExpressions;
using MesData.Common;
using System.Threading;

namespace PPAGUI
{
    public partial class Main : KryptonForm
    {
        #region CONSTRUCTOR
        public Main()
        {
            InitializeComponent();

#if MiniMe
            var  name = "PCBA & Pump Assy Minime";
#elif Ariel
            var name = "PCBA & Pump Assy Ariel";
#elif Gaia
            var name = "PCBA & Pump Assy GAIA";
            panelBluetooth.Visible = true;
#endif
            Text = name + @" V1.5";
            _mesData = new Mes("Repair", AppSettings.Resource, name);

            lbTitle.Text = AppSettings.Resource;

            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
            _pcbaDataConfig?.SaveToFile();

            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
            _pumpDataConfig?.SaveToFile();
           
            kryptonNavigator1.SelectedIndex = 0;
            EventLogUtil.LogEvent("Application Start");

            //Prepare Maintenance Grid
            var maintStrings = new[] { "Resource", "MaintenanceType", "MaintenanceReq", "NextDateDue", "NextThruputQtyDue", "MaintenanceState" };
           
            for (int i = 0; i < Dg_Maintenance.Columns.Count; i++)
            {
                if (!maintStrings.Contains(Dg_Maintenance.Columns[i].DataPropertyName))
                {
                    Dg_Maintenance.Columns[i].Visible = false;
                }
                else
                {
                    switch (Dg_Maintenance.Columns[i].HeaderText)
                    {

                        case "MaintenanceType":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance Type";
                            break;
                        case "MaintenanceReq":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance Requirement";
                            break;
                        case "NextDateDue":
                            Dg_Maintenance.Columns[i].HeaderText = @"Next Due Date";
                            break;
                        case "NextThruputQtyDue":
                            Dg_Maintenance.Columns[i].HeaderText = @"Next Thruput Quantity Due";
                            break;
                        case "MaintenanceState":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance State";
                            _indexMaintenanceState = Dg_Maintenance.Columns[i].Index;
                            break;
                    }

                }
            }
            //Instantiate Setting
            var setting = new Settings();
            InitStandByTimer(setting.WeighingDatabaseConnection, this);
            //Init Com
            var serialCom = new SerialPort
            {
                PortName = setting.PortName,
                BaudRate = setting.BaudRate,
                Parity = setting.Parity,
                DataBits = setting.DataBits,
                StopBits = setting.StopBits
            };

            _keyenceRs232Scanner = new Rs232Scanner(serialCom);
            _keyenceRs232Scanner.OnDataReadValid += KeyenceDataReadValid;

            _syncWorker.WorkerReportsProgress = true;
            _syncWorker.RunWorkerCompleted += SyncWorkerCompleted;
            _syncWorker.ProgressChanged += SyncWorkerProgress;
            _syncWorker.DoWork += SyncDoWork;

            _moveWorker = new AbortableBackgroundWorker();
            _moveWorker.WorkerReportsProgress = true;
            _moveWorker.RunWorkerCompleted += MoveWorkerCompleted;
            _moveWorker.ProgressChanged += MoveWorkerProgress;
            _moveWorker.DoWork += MoveWorkerDoWork;
        }

        private void MoveWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            /*Move In, Move*/
            var serial = (string) e.Argument;
            try
            {
                var oContainerStatus = Mes.GetContainerStatusDetails(_mesData, serial);
                if (oContainerStatus.ContainerName != null)
                {
                    _moveWorker.ReportProgress(1, @"Container Move In Attempt 1");
                    var transaction = Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                    var resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                    if (!resultMoveIn && transaction.Message.Contains("TimeOut"))
                    {
                        _moveWorker.ReportProgress(1, @"Container Move In Attempt 2");
                        transaction = Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                        resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                        if (!resultMoveIn && transaction.Message.Contains("TimeOut"))
                        {
                            _moveWorker.ReportProgress(1, @"Container Move In Attempt 3");
                            transaction = Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                            resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                        }
                    }
                    if (resultMoveIn)
                    {
                        ThreadHelper.ControlSetText(lbMoveIn, _dMoveIn.ToString(Mes.DateTimeStringFormat));

                        //Component Consume
                        var listIssue = new List<dynamic>();
                        if (_afterRepair)
                        {
                            if (_pumpEnabled) listIssue.Add(_pumpData.ToIssueActualDetail("Repair"));
                            if (_pcbaEnabled) listIssue.Add(_pcbaData.ToIssueActualDetail("Repair"));
                        }
                        else
                        {
                            if (_pumpEnabled && _pernahScanPumpValue != _pumpData.RawData) listIssue.Add(_pumpData.ToIssueActualDetail(_pernahScanPumpValue != "" ?  "Repair" : null));
                            if (_pcbaEnabled && _pernahScanPcbaValue != _pcbaData.RawData) listIssue.Add(_pcbaData.ToIssueActualDetail(_pernahScanPcbaValue != "" ? "Repair" : null));
                        }
                      
                        var consume = TransactionResult.Create(true);
                        if (listIssue.Count > 0)
                        {
                            _moveWorker.ReportProgress(2, @"Container Component Issue.");;
                            consume = Mes.ExecuteComponentIssue(_mesData, oContainerStatus.ContainerName.Value,
                                listIssue);
                        }

                        if (consume.Result || listIssue.Count <= 0)
                        {
                            var attrs = new List<ContainerAttrDetail>();

                            if (_pumpEnabled || _pcbaEnabled)
                            {
                                
                                if (_pumpEnabled)
                                {
                                    attrs.AddRange(new[]
                                    {
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = "PumpModel", AttributeValue = _pumpData.PartNumber.Value,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    },
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = "PumpSn", AttributeValue = _pumpData.RawData,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    },
                                                });
                                }
                                if (_pcbaEnabled)
                                {
                                    attrs.AddRange(new[]
                                    {
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = "PcbaSn" ,AttributeValue = _pcbaData.RawData,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    }
                                                });
                                }
                                
                            }
#if Gaia
                            if (_isBluetoothProduct)
                            {
                                attrs.AddRange(new[]
                                {
                                    new ContainerAttrDetail
                                    {
                                        Name = "GaiaBluetoothMac" ,AttributeValue = _scannedMacAdress,
                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                    }
                                });
                            }

#endif
                            if (attrs?.Count > 0)
                            {
                                Mes.ExecuteContainerAttrMaint(_mesData,
                                    oContainerStatus, attrs.ToArray());
                            }
#if Gaia
                            if (_isBluetoothProduct)
                            {
                                var maint = new ContainerMaintDetail
                                {
                                    wikBTMAC = _scannedMacAdress,
                                };
                                var btUpdate = Mes.ExecuteContainerMaintenance(_mesData, oContainerStatus.ContainerName.ToString(), maint);
                                if (!btUpdate)
                                {
                                    e.Result = PPAState.BluetoothUpdateFail;
                                    return;
                                }
                            }
#endif
                            _dbMoveOut = DateTime.Now;
                            _moveWorker.ReportProgress(3, @"Container Move Standard Attempt 1");
                            var resultMoveStd = Mes.ExecuteMoveStandard(_mesData,
                                oContainerStatus.ContainerName.Value, _dbMoveOut);
                            if (!resultMoveStd.Result)
                            {
                                _moveWorker.ReportProgress(3, @"Get Container Position 1");
                                var posAfterMoveStd = Mes.GetCurrentContainerStep(_mesData, oContainerStatus.ContainerName.Value);
                                resultMoveStd.Result |= !posAfterMoveStd.Contains("PCBA");
                                if (!resultMoveStd.Result)
                                {
                                    _dbMoveOut = DateTime.Now;
                                    _moveWorker.ReportProgress(3, @"Container Move Standard Attempt 2");
                                    resultMoveStd = Mes.ExecuteMoveStandard(_mesData,
                                        oContainerStatus.ContainerName.Value, _dbMoveOut);

                                    if (!resultMoveStd.Result)
                                    {
                                        _moveWorker.ReportProgress(3, @"Get Container Position 2");
                                        posAfterMoveStd = Mes.GetCurrentContainerStep(_mesData, oContainerStatus.ContainerName.Value);
                                        resultMoveStd.Result |= !posAfterMoveStd.Contains("PCBA");
                                        if (!resultMoveStd.Result)
                                        {
                                            _dbMoveOut = DateTime.Now;
                                            _moveWorker.ReportProgress(3, @"Container Move Standard Attempt 3");
                                            resultMoveStd = Mes.ExecuteMoveStandard(_mesData,
                                                oContainerStatus.ContainerName.Value, _dbMoveOut);
                                            if (!resultMoveStd.Result)
                                            {
                                                _moveWorker.ReportProgress(3, @"Get Container Position 3");
                                                posAfterMoveStd = Mes.GetCurrentContainerStep(_mesData, oContainerStatus.ContainerName.Value);
                                                resultMoveStd.Result |= !posAfterMoveStd.Contains("PCBA");
                                            }
                                        }
                                    }
                                }
                            }

                            if (resultMoveStd.Result)
                            {
                                ThreadHelper.ControlSetText(lbMoveOut, _dbMoveOut.ToString(Mes.DateTimeStringFormat));
                                //Update Counter
                                var currentPos = Mes.GetCurrentContainerStep(_mesData, oContainerStatus.ContainerName.Value);
                                Mes.UpdateOrCreateFinishGoodRecordToCached(_mesData, oContainerStatus.MfgOrderName?.Value, oContainerStatus.ContainerName.Value, currentPos);

                                _mesUnitCounter.UpdateCounter(oContainerStatus.ContainerName.Value);
                                MesUnitCounter.Save(_mesUnitCounter);

                                ThreadHelper.ControlSetText(Tb_PpaQty, _mesUnitCounter.Counter.ToString());
                            }

                            e.Result = resultMoveStd.Result
                                ? PPAState.ScanUnitSerialNumber
                                : PPAState.MoveInOkMoveFail;
                        }
                        else
                        {
                            e.Result = PPAState.ComponentIssueFailed;
                        }

                    }
                    else
                    {// check if fail by maintenance Past Due
                       var  transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                        if (transPastDue.Result)
                        {
                            KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        e.Result = (PPAState.MoveInFail);
                    }
                }
                else e.Result=(PPAState.UnitNotFound);


            }
            catch (Exception ex)
            {
                e.Result = PPAState.Done;
            }
        }

        private void MoveWorkerProgress(object sender, ProgressChangedEventArgs e)
        {
            var command = (string) e.UserState;
            lblCommand.Text = command;
        }

        private void MoveWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            var states = (PPAState) e.Result;
            //_startStandByTimer = 1;
            SetPpaState(states);
        }
        #region Auto Standby
        public void InitStandByTimer(string connection, Form parentForm)
        {
            _standByConnection = connection;
            _autoStandBy = new AutoStandBy(_mesData, parentForm)
            {
                StandbyStatus = "PPA - Standby Time",
                DefaultPreStandbyStatus = "PPA - Productive Time",
                DefaultPreStandbyStatusReason = "Pass",
                MaintenanceStatus = "PPA - Internal Downtime"
            };
            _autoStandBy.AutoStandByStatusUpdated += AutoStandByStatusUpdated;
            _autoStandBy.Init(connection);
            tbPrePopUp.Text = _autoStandBy.AutoStandBySetting.PrePopUpTimer.ToString("0.##");
            tbPreStandBy.Text = _autoStandBy.AutoStandBySetting.PreStandByTimer.ToString("0.##");
            _startStandByTimer = 1;
            while (_startStandByTimer != 0)
            {
                Thread.Sleep(1);
            }
            tmrAutoStandByChecker.Start();
        }

        private void AutoStandByStatusUpdated(StandByState standbystate)
        {
            GetStatusOfResource();
        }


        public void RestoreStatusPreStandBy()
        {
            _autoStandBy.RestoreStatusPreStandBy(_autoStandBy.StatusPreStandby);
        }

        public void StartStandByTimer()
        {
            _autoStandBy?.StartStandByTimer();
        }
        public void StopStandByTimer()
        {
            _autoStandBy?.StopPopUpTimer();
            _autoStandBy?.StopStandByTimer();
        }
        #endregion

        private   void  KeyenceDataReadValid(object sender)
        {
            if (!_readScanner) Tb_Scanner.Clear();
            _ignoreScanner = true;
          
            if (string.IsNullOrEmpty(_keyenceRs232Scanner.DataValue) ) return;
            var temp = _keyenceRs232Scanner.DataValue.Trim('\r', '\n');
            if (_keyenceRs232Scanner.DataValue.Length!=19) return;
            switch (_ppaState)
            {
                case PPAState.ScanUnitSerialNumber:
                    Tb_SerialNumber.Text = temp;
                    Tb_Scanner.Clear();
                      SetPpaState(PPAState.CheckUnitStatus);
                    break;
            }
            _ignoreScanner = false;
            Tb_Scanner.Clear();
        }
        public sealed override string Text
        {
            get => base.Text;
            set => base.Text = value;
        }

        #endregion

        #region INSTANCE VARIABLE

        private PPAState _ppaState;
        private readonly Mes _mesData;
        private DateTime _dMoveIn;
        private PcbaData _pcbaData;
        private PumpData _pumpData;
        private PcbaDataPointConfig _pcbaDataConfig;
        private PumpDataPointConfig _pumpDataConfig;
        private PcbaDataPointConfig _tempPcba;
        private PumpDataPointConfig _tempPump;
        private string _wrongOperationPosition;
        private MesUnitCounter _mesUnitCounter;
        #endregion

        #region FUNCTION USEFULL

        private void SetPpaState(PPAState newPpaState)
        {
            _ppaState = newPpaState;
            _startStandByTimer = 2;
            while (_startStandByTimer != 0)
            {
                Thread.Sleep(1);
            }
            switch (_ppaState)
            {
                case PPAState.PlaceUnit:
                    _readScanner = false;
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = @"Resource is not in ""Up"" condition!";
                    break;
                case PPAState.ScanUnitSerialNumber:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.LimeGreen;
                    lblCommand.Text = @"Scan Unit Serial Number!";
                    ClrContainer();
                    _keyenceRs232Scanner.StopRead();

                    _pcbaData = new PcbaData();
                    _pumpData = new PumpData();

                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        SetPpaState(PPAState.PlaceUnit);
                        break;
                    }

                    // check if fail by maintenance Past Due
                    var transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                    if (transPastDue.Result)
                    {
                        KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }

                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    ActiveControl = Tb_Scanner;
                    _keyenceRs232Scanner.StartRead();
                    break;
                case PPAState.CheckUnitStatus:
                    btnResetState.Enabled = false;
                    _afterRepair = false;
                    _readScanner = false;
                    _oldPcba = "";
                    _oldPump = "";
                    _keyenceRs232Scanner.StopRead();
                    //RestoreStatusPreStandBy();
                    lblCommand.Text = @"Checking Unit Status";
                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        SetPpaState(PPAState.PlaceUnit);
                        break;
                    }

                    // check if fail by maintenance Past Due
                    transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                    if (transPastDue.Result)
                    {
                        KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }

                    var oContainerStatus =
                        Mes.GetContainerStatusDetails(_mesData, Tb_SerialNumber.Text, _mesData.DataCollectionName);
                    if (oContainerStatus != null)
                    {

                        if (oContainerStatus.Operation != null)
                        {
                            if (oContainerStatus.Qty == 0)
                            {
                                _wrongOperationPosition = "Scrap";
                                SetPpaState(PPAState.WrongOperation);
                                break;
                            }

                            if (oContainerStatus.Operation.Name != _mesData.OperationName)
                            {
                                _wrongOperationPosition = oContainerStatus.Operation.Name;
                                SetPpaState(PPAState.WrongOperation);
                                break;
                            }


                        }
                        //Check if bluetooth
#if Gaia
                        var s = oContainerStatus.ContainerName.ToString();
                        _isBluetoothProduct = s.Length > 14 && (s[13] == '3');
#endif
                        _dMoveIn = DateTime.Now;
                        lbMoveIn.Text = _dMoveIn.ToString(Mes.DateTimeStringFormat);
                        lbMoveOut.Text = "";
                        //get AfterRepair Atribute
                        var afterRepair = Mes.GetStringAttribute(oContainerStatus.Attributes, "AfterRepair", "No");
                        _afterRepair = afterRepair == "Yes";
                        lbAfterRepair.Text = _afterRepair ? "Yes" : "No";
                        //get Change Component
                        var changeComponent =
                            Mes.GetStringAttribute(oContainerStatus.Attributes, "ChangeComponent", "False");
                        _changeComponent = changeComponent == "True";
                        var changePcba = Mes.GetStringAttribute(oContainerStatus.Attributes, "ChangePcba", "False");
                        _changePcba = changePcba == "True";
                        var changePump = Mes.GetStringAttribute(oContainerStatus.Attributes, "ChangePump", "False");
                        _changePump = changePump == "True";
                        
                        if (!_afterRepair)
                        {
                            _pernahScanPumpValue = Mes.GetStringAttribute(oContainerStatus.Attributes, "PumpSn", "");
                            _pernahScanPcbaValue = Mes.GetStringAttribute(oContainerStatus.Attributes, "PcbaSn", "");
                        }

                        ppaScanBindingSource.Clear();
                        if (_changeComponent)
                        {
                            _scanlistSn = ppaScanBindingSource.Add(new PpaScan
                                {ScanningList = "Appliacne S/N", Status = "Completed"});
                        }

                        if (_changePcba)
                        {
                            _scanlistPcba = new PpaScan {ScanningList = "PCBA QR Code"};
                            _scanlistPcbaIdx = ppaScanBindingSource.Add(_scanlistPcba);
                            var oldPcba = oContainerStatus.Attributes?.Where(x => x.Name == "PcbaSn").ToList();
                            if (oldPcba != null && oldPcba.Count > 0)
                            {
                                _oldPcba = oldPcba[0].AttributeValue.Value;
                            }
                        }

                        if (_changePump)
                        {
                            _scanlistPump = new PpaScan {ScanningList = "Pump QR Code"};
                            _scanlistPumpIdx = ppaScanBindingSource.Add(_scanlistPump);
                            var oldPump = oContainerStatus.Attributes?.Where(x => x.Name == "PumpSn").ToList();
                            if (oldPump != null && oldPump.Count > 0)
                            {
                                _oldPump = oldPump[0].AttributeValue.Value;
                            }
                        }

                        kryptonDataGridView2.Visible = _changeComponent || _changePcba || _changePump;

                        if (oContainerStatus.MfgOrderName != null && _mesData.ManufacturingOrder == null ||
                            _mesData.ManufacturingOrder?.Name != oContainerStatus.MfgOrderName)
                        {
                            if (oContainerStatus.MfgOrderName != null)
                            {
                                lblLoadingPo.Visible = true;
                                var mfg = Mes.GetMfgOrder(_mesData, oContainerStatus.MfgOrderName.ToString());

                                if (mfg == null)
                                {
                                    lblLoadingPo.Visible = false;
                                    KryptonMessageBox.Show(this, "Failed To Get Manufacturing Order Information",
                                        "Check Unit",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);

                                    SetPpaState(PPAState.ScanUnitSerialNumber);
                                    break;
                                }

                                _mesData.SetManufacturingOrder(mfg);
                                Tb_PO.Text = oContainerStatus.MfgOrderName.ToString();
                                Tb_Product.Text = oContainerStatus.Product.Name;
                                Tb_ProductDesc.Text = oContainerStatus.ProductDescription.Value;
                                var img = Mes.GetImage(_mesData, oContainerStatus.Product.Name);
                                if (img != null) pictureBox1.ImageLocation = img.Identifier.Value;

                                if (_mesUnitCounter != null)
                                {
                                    _mesUnitCounter.StopPoll();
                                }

                                _mesUnitCounter = MesUnitCounter.Load(MesUnitCounter.GetFileName(mfg.Name.Value));

                                _mesUnitCounter.SetActiveMfgOrder(mfg.Name.Value);

                                _mesUnitCounter.InitPoll(_mesData);
                                _mesUnitCounter.StartPoll();
                                MesUnitCounter.Save(_mesUnitCounter);

                                Tb_PpaQty.Text = _mesUnitCounter.Counter.ToString();
                                lblLoadingPo.Visible = false;
                            }
                        }

                        if (!_afterRepair)
                        {
                            _pcbaEnabled = _pcbaDataConfig.Enable == EnableDisable.Enable;
                            _pumpEnabled = _pumpDataConfig.Enable == EnableDisable.Enable;
                        }
                        else
                        {
                            _pcbaEnabled = _pcbaDataConfig.Enable == EnableDisable.Enable && _changePcba;
                            _pumpEnabled = _pumpDataConfig.Enable == EnableDisable.Enable && _changePump;
                        }


                        if (_pcbaEnabled && _pumpEnabled)
                        {
                            SetPpaState(PPAState.ScanPcbaOrPumpSerialNumber);
                            break;
                        }

                        if (!_pcbaEnabled && !_pumpEnabled)
                        {
#if Gaia
                            if (_isBluetoothProduct)
                            {
                                SetPpaState(PPAState.ScanMacAddress);
                                break;
                            }
#endif
                            SetPpaState(PPAState.UpdateMoveInMove);
                            break;
                        }

                        if (!_pcbaEnabled)
                        {
                            SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }

                        if (!_pumpEnabled)
                        {
                            SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }


                    }

                    var containerStep =
                        Mes.GetCurrentContainerStep(_mesData, Tb_SerialNumber.Text); // try get operation pos
                    if (containerStep != null && !_mesData.OperationName.Contains(containerStep))
                    {
                        _wrongOperationPosition = containerStep;
                        SetPpaState(PPAState.WrongOperation);
                        break;
                    }

                    SetPpaState(PPAState.UnitNotFound);
                    break;
                case PPAState.UnitNotFound:
                    btnResetState.Enabled = true;
                    _readScanner = false;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = "Unit Not Found";
                    break;
                case PPAState.ScanPcbaSerialNumber:
                    btnResetState.Enabled = true;
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = "Scan PCBA Serial Number!";
                    break;
                case PPAState.ScanPumpSerialNumber:
                    btnResetState.Enabled = true;
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = "Scan Pump Serial Number!";
                    break;
                case PPAState.ScanPcbaOrPumpSerialNumber:
                    btnResetState.Enabled = true;
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = @"Scan Pump Or PCBA Serial Number!";
                    break;
                case PPAState.ScanMacAddress:
                    btnResetState.Enabled = true;
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = @"Scan Bluetooth MAC Address!";
                    break;
                case PPAState.Done:
                    break;
                case PPAState.UpdateMoveInMove:
                    _readScanner = false;
                    btnResetState.Enabled = false;
                   // _startStandByTimer = 2;
                   // RestoreStatusPreStandBy();
                    _moveWorker.RunWorkerAsync(Tb_SerialNumber.Text);
                    break;
                case PPAState.MoveSuccess:
                    btnResetState.Enabled = true;
                    break;
                case PPAState.MoveInOkMoveFail:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Container Move Standard Fail";
                    break;
                case PPAState.MoveInFail:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Container Move In Fail";
                    break;
                case PPAState.WrongOperation:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = $@"Completed Scan, Container in {_wrongOperationPosition}";
                    break;
                case PPAState.ComponentNotFound:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Cannot Find Component in Bill of Material";
                    break;
                case PPAState.WrongComponent:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Wrong Component";
                    break;
                case PPAState.WrongProductionOrder:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Mismatch Production Order";
                    break;
                case PPAState.ComponentIssueFailed:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Component Issue Failed.";
                    break;
                case PPAState.WaitPreparation:
                    btnResetState.Enabled = true;
                    ClearPo();
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Wait For Preparation";
                    btnStartPreparation.Enabled = true;
                    break;
                case PPAState.SamePcba:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Same PCBA, Please Replace with the New PCBA";
                    KryptonMessageBox.Show(this, "Same PCBA, Please Replace with the New PCBA", "Scan Pcba",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                case PPAState.SamePump:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Same Pump, Please Replace with the New Pump";
                    KryptonMessageBox.Show(this, "Same Pump, Please Replace with the New Pump", "Scan Pump",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                case PPAState.BluetoothUpdateFail:
                    btnResetState.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Bluetooth Data Update Failed";
                    break;
               
            }

            switch (_ppaState)
            {
                case PPAState.ScanUnitSerialNumber:
                case PPAState.MoveInOkMoveFail:
                case PPAState.MoveInFail:
                case PPAState.WrongOperation:
                case PPAState.ComponentNotFound:
                case PPAState.Done:
                case PPAState.WrongComponent:
                case PPAState.WrongProductionOrder:
                case PPAState.ComponentIssueFailed:
                case PPAState.SamePcba:
                case PPAState.SamePump:
                case PPAState.ScanMacAddress:
                case PPAState.BluetoothUpdateFail:
                  //  _startStandByTimer = 1;
                    break;

            }
            _startStandByTimer = 1;
            while (_startStandByTimer != 0)
            {
                Thread.Sleep(1);
            }
        }

        private void ClrContainer()
        {
            Tb_Scanner.Clear();
            Tb_SerialNumber.Clear();
            Tb_PumpSerialNumber.Clear();
            Tb_PCBASerialNumber.Clear();
            Tb_PCBAPartNumber.Clear();
            Tb_PumpPartNumber.Clear();
            ppaScanBindingSource.Clear();
            TB_BluetoothMacAddress.Clear();
            TB_BluetoohPartNumber.Clear();
            kryptonDataGridView2.Visible = false;
        }

        #endregion

        #region FUNCTION STATUS OF RESOURCE

        private void GetStatusMaintenanceDetails()
        {
            try
            {
                var maintenanceStatusDetails =   Mes.GetMaintenanceStatusDetails(_mesData);
                _mesData.SetMaintenanceStatusDetails(maintenanceStatusDetails);
                if (maintenanceStatusDetails != null)
                {
                    getMaintenanceStatusDetailsBindingSource.DataSource =
                        new BindingList<GetMaintenanceStatusDetails>(maintenanceStatusDetails);
                    Dg_Maintenance.DataSource = getMaintenanceStatusDetailsBindingSource;

                    //get past due, warning, and tolerance
                    var pastDue = maintenanceStatusDetails.Where(x => x.MaintenanceState=="Past Due").ToList();
                    var due = maintenanceStatusDetails.Where(x => x.MaintenanceState == "Due").ToList();
                    var pending = maintenanceStatusDetails.Where(x => x.MaintenanceState == "Pending").ToList();

                    if (pastDue.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Past Due";
                        lblResMaintMesg.BackColor = Color.Red;
                        lblResMaintMesg.Visible = true;
                        if (_mesData?.ResourceStatusDetails?.Reason?.Name != "Planned Maintenance")
                        {
                              Mes.SetResourceStatus(_mesData, "PPA - Planned Downtime", "Planned Maintenance");
                        }
                        return;
                    }
                    if (due.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Due";
                        lblResMaintMesg.BackColor = Color.Orange;
                        lblResMaintMesg.Visible = true;
                        return;
                    }
                    if (pending.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Pending";
                        lblResMaintMesg.BackColor = Color.Yellow;
                        lblResMaintMesg.Visible = true;
                        return;
                    }
                }
                lblResMaintMesg.Visible = false;
                lblResMaintMesg.Text = "";
                getMaintenanceStatusDetailsBindingSource.DataSource = null;
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private void GetStatusOfResource()
        {
            try
            {
                var resourceStatus =   Mes.GetResourceStatusDetails(_mesData);
                if (resourceStatus != null)
                {
                    _mesData.SetResourceStatusDetails(resourceStatus);
                    if (resourceStatus.Status != null) Tb_StatusCode.Text = resourceStatus.Reason?.Name;
                    // if (resourceStatus.Reason?.Name != "Standby") _autoStandBy?.ResetStandBy();
                    if (resourceStatus.Reason?.Name == "Standby") _autoStandBy.SetToStandBy(); else _autoStandBy.ResetStandBy();
                    if (resourceStatus.Availability != null)
                    {
                        if (resourceStatus.Availability.Value == "Up")
                        {
                            Tb_StatusCode.StateCommon.Content.Color1 = resourceStatus.Reason?.Name == "Standby" ? Color.Yellow : Color.Green;
                        }
                        else if (resourceStatus.Availability.Value == "Down")
                        {
                            Tb_StatusCode.StateCommon.Content.Color1 = Color.Red;
                        }
                    }
                    else
                    {
                        Tb_StatusCode.StateCommon.Content.Color1 = Color.Orange;
                    }

                    if (resourceStatus.TimeAtStatus != null)
                        Tb_TimeAtStatus.Text = $@"{Mes.OaTimeSpanToString(resourceStatus.TimeAtStatus.Value)}";
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private void GetStatusOfResourceDetail()
        {
            try
            {
                var resourceStatus =   Mes.GetResourceStatusDetails(_mesData);
                if (resourceStatus != null)
                {
                    _mesData.SetResourceStatusDetails(resourceStatus);
                    if (resourceStatus.Status != null) Cb_StatusCode.Text = resourceStatus.Status.Name;
                      Task.Delay(1000);
                    if (resourceStatus.Reason != null) Cb_StatusReason.Text = resourceStatus.Reason.Name;
                    if (resourceStatus.Availability != null)
                    {
                        Tb_StatusCodeM.Text = resourceStatus.Availability.Value;
                        if (resourceStatus.Availability.Value == "Up")
                        {
                            Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Green;
                        }
                        else if (resourceStatus.Availability.Value == "Down")
                        {
                            Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Red;
                        }
                    }
                    else
                    {
                        Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Orange;
                    }

                    if (resourceStatus.TimeAtStatus != null)
                        Tb_TimeAtStatus.Text = $@"{Mes.OaTimeSpanToString(resourceStatus.TimeAtStatus.Value)}";
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source
                    ? MethodBase.GetCurrentMethod()?.Name
                    : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private void GetResourceStatusCodeList()
        {
            try
            {
                var oStatusCodeList =   Mes.GetListResourceStatusCode(_mesData);
                if (oStatusCodeList != null)
                {
                    Cb_StatusCode.DataSource = oStatusCodeList.Where(x=>x.Name.IndexOf("PPA", StringComparison.Ordinal)==0).ToList();
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        #endregion

        #region COMPONENT EVENT

        private   void TimerRealtime_Tick(object sender, EventArgs e)
        {
              GetStatusOfResource();
              GetStatusMaintenanceDetails();
        }
        private   void btnResetState_Click(object sender, EventArgs e)
        {
            if (_ppaState == PPAState.WaitPreparation) return;
              SetPpaState(PPAState.ScanUnitSerialNumber);
            Tb_Scanner.Focus();
        }

        private bool _readScanner;
        private bool _ignoreScanner;
        private DateTime _dbMoveOut;
        private readonly int _indexMaintenanceState=0;
        private bool _afterRepair = false;
        private bool _changeComponent;
        private bool _changePcba;
        private bool _changePump;
        private PpaScan _scanlistPcba;
        private PpaScan _scanlistPump;
        private string _oldPcba;
        private string _oldPump;
        private bool _pcbaEnabled;
        private bool _pumpEnabled;
        private int _scanlistPcbaIdx;
        private int _scanlistPumpIdx;
        private readonly Rs232Scanner _keyenceRs232Scanner;
        private bool _allowClose;
        private int _scanlistSn;
        private bool _sortAscending;
        private BindingList<FinishedGood> _bindingList;

        private   void Tb_Scanner_KeyUp(object sender, KeyEventArgs e)
        {
            if (!_readScanner) Tb_Scanner.Clear();
            if (_ignoreScanner) e.Handled = true;
            if (e.KeyCode == Keys.Enter)
            {
                _ignoreScanner = true;
                if (string.IsNullOrEmpty(Tb_Scanner.Text)) return;
                switch (_ppaState)
                {
                    case PPAState.ScanUnitSerialNumber:
                        Tb_SerialNumber.Text = Tb_Scanner.Text.Trim();
                        Tb_Scanner.Clear();
                        SetPpaState(PPAState.CheckUnitStatus);
                        break;
                    case PPAState.ScanPcbaSerialNumber:
                    case PPAState.ScanPumpSerialNumber:
                    case PPAState.ScanPcbaOrPumpSerialNumber:
                        var scannedPcba = Tb_Scanner.Text.Trim();
                        Tb_Scanner.Clear();
                        if (scannedPcba.IndexOf("135", StringComparison.Ordinal) == 0 && _pcbaEnabled)
                        {
                            var transactPcba = PcbaData.ParseData(scannedPcba, _pcbaDataConfig);
                            if (transactPcba.Result)
                            {
                                _pcbaData = (PcbaData)transactPcba.Data;
                                var s = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product?.Name == _pcbaData.PartNumber.Value).ToList();
                                if (s == null || s.Count == 0)
                                {
                                      SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }

                                if (s[0].wikScanning!= "X")
                                {
                                      SetPpaState(PPAState.WrongComponent);
                                    break;
                                }

                                if (_afterRepair)
                                {
                                    if (_oldPcba == scannedPcba)
                                    {
                                          SetPpaState(PPAState.SamePcba);
                                        break;
                                    }
                                }
                                Tb_PCBAPartNumber.Text = _pcbaData.PartNumber?.Value;
                                Tb_PCBASerialNumber.Text = scannedPcba;
                                _pcbaData.SetQtyRequired(s[0].QtyRequired == null ? 0: s[0].QtyRequired.Value);
                                if (_afterRepair && _scanlistPcba!=null)
                                {
                                    _scanlistPcba.Status = "Completed";
                                    ppaScanBindingSource[_scanlistPcbaIdx] = _scanlistPcba;
                                }
                                if (!_pumpEnabled || !string.IsNullOrEmpty(_pumpData.RawData))
                                {
                                    if (_isBluetoothProduct)
                                    {
                                        SetPpaState(PPAState.ScanMacAddress);
                                        break;
                                    }
                                    SetPpaState(PPAState.UpdateMoveInMove);
                                    break;
                                }
                            }
                            else
                            {
                                var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                                var str = scannedPcba.Substring(0, l);
                                var vPcba = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPcba == null || vPcba.Count == 0)
                                {
                                      SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }
                        if (scannedPcba.IndexOf("103", StringComparison.Ordinal) == 0 &&_pumpEnabled)
                        {
                            var scannedPump = scannedPcba;
                            var transactPump = PumpData.ParseData(scannedPump, _pumpDataConfig);
                            if (transactPump.Result)
                            {
                                _pumpData = (PumpData)transactPump.Data;
                                var s = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product?.Name == _pumpData.PartNumber.Value).ToList();
                                if (s == null || s.Count == 0)
                                {
                                      SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                if (s[0].wikScanning != "X")
                                {
                                      SetPpaState(PPAState.WrongComponent);
                                    break;
                                }
                                if (_afterRepair)
                                {
                                    if (_oldPump == scannedPump)
                                    {
                                          SetPpaState(PPAState.SamePump);
                                        break;
                                    }
                                }
                                Tb_PumpPartNumber.Text = _pumpData.PartNumber?.Value;
                                Tb_PumpSerialNumber.Text = scannedPump;
                                _pumpData.SetQtyRequired(s[0].QtyRequired == null? 0 : s[0].QtyRequired.Value);
                                if (_afterRepair && _scanlistPump != null)
                                {
                                    _scanlistPump.Status = "Completed";
                                    ppaScanBindingSource[_scanlistPumpIdx] = _scanlistPump;
                                }

                                if (!_pcbaEnabled || !string.IsNullOrEmpty(_pcbaData.RawData))
                                {
#if Gaia
                                    if (_isBluetoothProduct)
                                    {
                                        SetPpaState(PPAState.ScanMacAddress);
                                        break;
                                    }
#endif

                                    SetPpaState(PPAState.UpdateMoveInMove);
                                    break;
                                }
                            }
                            else
                            {
                                var l = scannedPump.Length > 10 ? 10 : scannedPump.Length;
                                var str = scannedPump.Substring(0, l);
                                var vPump = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPump == null || vPump.Count == 0)
                                {
                                      SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }
                        else
                        {
                            Tb_Scanner.Clear();
                            var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                            var str = scannedPcba.Substring(0, l);
                            var vPcba = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                            if (vPcba == null || vPcba.Count == 0)
                            {
                                  SetPpaState(PPAState.ComponentNotFound);
                                break;
                            }
                            SetPpaState(PPAState.WrongComponent);
                            break;
                        }
                    case PPAState.ScanMacAddress:
                        var scannedMacAddress = Tb_Scanner.Text.Trim();
                        var valid = ValidateMac(scannedMacAddress);
                        if (valid)
                        {
                            TB_BluetoothMacAddress.Text = scannedMacAddress;
                            TB_BluetoohPartNumber.Text = scannedMacAddress;
                            _scannedMacAdress = scannedMacAddress;
                            SetPpaState(PPAState.UpdateMoveInMove);
                        }
                        else
                        {
                            TB_BluetoothMacAddress.Text = "";
                            _scannedMacAdress = "";
                            SetPpaState(PPAState.WrongComponent);
                        }
                        break;

                }
                _ignoreScanner = false;
                Tb_Scanner.Clear();
            }
        }

        private bool ValidateMac(string scannedMacAddress)
        {
            var temp = scannedMacAddress.Replace(":", "").Replace("-","");
            var r = new Regex(
                "^(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{2}){5}[0-9a-fA-F]{2}$");
            return r.IsMatch(temp) && temp.Length==12;

        }

        #endregion

        private   void Main_Load(object sender, EventArgs e)
        {
              ClearPo();
              GetStatusOfResource();
              GetStatusMaintenanceDetails();
              GetResourceStatusCodeList();
              SetPpaState(PPAState.WaitPreparation);
        }

        private void ClearPo()
        {
            Tb_PO.Clear();
            Tb_Product.Clear();
            Tb_ProductDesc.Clear();
            Tb_PpaQty.Clear();
            Tb_FinishedGoodCounter.Clear();
            pictureBox1.ImageLocation = null;
            ClrContainer();
        }

        private void kryptonGroupBox2_Panel_Paint(object sender, PaintEventArgs e)
        {

        }

        private   void Cb_StatusCode_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                var oStatusCode =   Mes.GetResourceStatusCode(_mesData, Cb_StatusCode.SelectedValue != null ? Cb_StatusCode.SelectedValue.ToString() : "");
                if (oStatusCode != null)
                {
                    Tb_StatusCodeM.Text = oStatusCode.Availability.ToString();
                    if (oStatusCode.ResourceStatusReasons != null)
                    {
                        var oStatusReason =   Mes.GetResourceStatusReasonGroup(_mesData, oStatusCode.ResourceStatusReasons.Name);
                        Cb_StatusReason.DataSource = oStatusReason.Entries;
                    }
                    else
                    {
                        Cb_StatusReason.Items.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private   void btnSetMachineStatus_Click(object sender, EventArgs e)
        {
            try
            {
                var result = false;
                if (Cb_StatusCode.Text != "" && Cb_StatusReason.Text != "")
                {
                    if (Cb_StatusReason.Text == "Standby") _autoStandBy.SetToStandBy();else _autoStandBy.ResetStandBy();
                    result =   Mes.SetResourceStatus(_mesData, Cb_StatusCode.Text, Cb_StatusReason.Text);
                }
                else if (Cb_StatusCode.Text != "")
                {
                    result =   Mes.SetResourceStatus(_mesData, Cb_StatusCode.Text, "");
                }

                GetStatusOfResourceDetail();
                GetStatusOfResource();
                KryptonMessageBox.Show(result ? "Setup status successful" : "Setup status failed");

            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private async  void kryptonNavigator1_SelectedPageChanged(object sender, EventArgs e)
        {
            if (kryptonNavigator1.SelectedIndex == 0)
            {
                ActiveControl = Tb_Scanner;
            }
            if (kryptonNavigator1.SelectedIndex == 1)
            {
                  GetStatusOfResourceDetail();
            }
            if (kryptonNavigator1.SelectedIndex == 2)
            {
                _tempPcba = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
                Ppg_Pcba.SelectedObject = _tempPcba;
                _tempPump = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
                Ppg_Pump.SelectedObject = _tempPump;
            }
            if (kryptonNavigator1.SelectedIndex == 3)
            {
                lblPo.Text = $@"Serial Number of PO: {_mesData.ManufacturingOrder?.Name}";
                lblLoading.Visible = true;
                  await GetFinishedGoodRecord();
                if (!_syncWorker.IsBusy)lblLoading.Visible = false;
            }

        }
        private async  Task GetFinishedGoodRecord()
        {
            if (_mesData == null) return;

            var data =   await Mes.GetFinishGoodRecordFromCached(_mesData, _mesData.ManufacturingOrder?.Name.ToString());
           
            if (data != null)
            {
                var list =   Mes.FinishGoodToFinishedGood(data);
                _bindingList = new BindingList<FinishedGood>(list);
                finishedGoodBindingSource.DataSource = _bindingList;
                kryptonDataGridView1.DataSource = finishedGoodBindingSource;
                Tb_FinishedGoodCounter.Text = list.Length.ToString();
            }
        }
        private void Btn_SetPcba_Click(object sender, EventArgs e)
        {
            try
            {
                _tempPcba.SaveToFile();
                _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
                KryptonMessageBox.Show("Pcba Setting Saved!");
            }
            catch
            {
                KryptonMessageBox.Show("Failed to save Pcba Setting");
            }
        }

        private void Btn_SetPump_Click(object sender, EventArgs e)
        {
            try
            {
                _tempPump.SaveToFile();
                _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
                KryptonMessageBox.Show("Pump Setting Saved!");
            }
            catch
            {
                KryptonMessageBox.Show("Failed to save Pump Setting");
            }

        }
        private void kryptonNavigator1_Selecting(object sender, ComponentFactory.Krypton.Navigator.KryptonPageCancelEventArgs e)
        {
            if (e.Index != 1 && e.Index != 2) return;

            using (var ss = new LoginForm24(e.Index == 1 ? "Maintenance" : "Quality"))
            {
                var dlg = ss.ShowDialog(this);
                if (dlg == DialogResult.Abort)
                {
                    KryptonMessageBox.Show("Login Failed");
                    e.Cancel = true;
                    return;
                }
                if (dlg == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (ss.UserDetails.UserRole == UserRole.Maintenance && e.Index != 1) e.Cancel = true;
                if (ss.UserDetails.UserRole == UserRole.Quality && e.Index != 2) e.Cancel = true;
            }


        }

        private   void btnCallMaintenance_Click(object sender, EventArgs e)
        {
            try
            {
                var dlg = MessageBox.Show(@"Are you sure want to call maintenance?", @"Call Maintenance",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dlg == DialogResult.No)
                {
                    return;
                }
                var result =   Mes.SetResourceStatus(_mesData, "PPA - Internal Downtime", "Maintenance");
                  GetStatusOfResource();
                KryptonMessageBox.Show(result ? "Setup status successful" : "Setup status failed");

            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

      
     

        private void Tb_Scanner_TextChanged(object sender, EventArgs e)
        {

        }


        private void Dg_Maintenance_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in Dg_Maintenance.Rows)
                {
                    switch (Convert.ToString(row.Cells[_indexMaintenanceState].Value))
                    {
                        //Console.WriteLine(Convert.ToString(row.Cells["MaintenanceState"].Value));
                        case "Pending":
                            row.DefaultCellStyle.BackColor = Color.Yellow;
                            break;
                        case "Due":
                            row.DefaultCellStyle.BackColor = Color.Orange;
                            break;
                        case "Past Due":
                            row.DefaultCellStyle.BackColor = Color.Red;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private void label28_Click(object sender, EventArgs e)
        {

        }

        private void Tb_FinishedGoodCounter_TextChanged(object sender, EventArgs e)
        {

        }

        private   void btnFinishPreparation_Click(object sender, EventArgs e)
        {
            if (_mesData.ResourceStatusDetails == null) return;
            if (_mesData.ResourceStatusDetails.Reason.Name == "Maintenance") return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Planned Maintenance") return;
            var result =   Mes.SetResourceStatus(_mesData, "PPA - Productive Time", "Pass");
              GetStatusOfResource();
            if (result)
            {
                btnFinishPreparation.Enabled = false;
                btnStartPreparation.Enabled = true;
                  SetPpaState(PPAState.ScanUnitSerialNumber);
            }
        }

        private   void btnStartPreparation_Click(object sender, EventArgs e)
        {
            ClearPo();
            if (_mesData.ResourceStatusDetails == null) return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Maintenance") return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Planned Maintenance") return;
            _mesData.SetManufacturingOrder(null);
            var result =   Mes.SetResourceStatus(_mesData, "PPA - Planned Downtime", "Preparation");
              GetStatusOfResource();
            if (result)
            {
                  SetPpaState(PPAState.WaitPreparation);
                btnFinishPreparation.Enabled = true;
                btnStartPreparation.Enabled = false;
            }
        }

        private  void button1_Click(object sender, EventArgs e)
        {
            var gg = new String[]{"KKK","mmm"}.Contains("kkk");
            var g = MesUnitCounter.Load("C:\\WIK-OPEX\\10037810.xti");
            using (var f = new StreamWriter(".\\containersVC.txt"))
            {
                foreach (var container in g.Containers)
                {
                    f.WriteLine(container);
                }
            }
         
        }
        //private   void  Closing()
        //{
        //    if (_mesUnitCounter != null)
        //    {
        //          _mesUnitCounter.StopPoll();
        //    }
        //    _allowClose = true;
        //    Close();
        //}
        private   void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                var dlg = MessageBox.Show(@"Are you sure want to close Application?", @"Close Application",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dlg == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (_allowClose)
            {
                e.Cancel = false;
                //Environment.Exit(Environment.ExitCode);
            }

            e.Cancel = false;
            //   Closing();
        }

        private BackgroundWorker _syncWorker = new BackgroundWorker();
        private readonly AbortableBackgroundWorker _moveWorker;
        private string _scannedMacAdress;
        private bool _isBluetoothProduct;
        private string _standByConnection;
        private AutoStandBy _autoStandBy;
        private int _startStandByTimer;
        private bool _pernahScanPump;
        private bool _pernahScanPcba;
        private string _pernahScanPumpValue;
        private string _pernahScanPcbaValue;

        private void SyncWorkerProgress(object sender, ProgressChangedEventArgs e)
        {

        }

        private void SyncWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            var data = (List<IFinishGoodRecord>) e.Result;
            var list = Mes.FinishGoodToFinishedGood(data);
            _bindingList = new BindingList<FinishedGood>(list);
            finishedGoodBindingSource.DataSource = _bindingList;
            kryptonDataGridView2.DataSource = finishedGoodBindingSource;
            Tb_FinishedGoodCounter.Text = list.Length.ToString();
            lblLoading.Visible = false;
        }
        private void SyncDoWork(object sender, DoWorkEventArgs e)
        {
            var temp = Mes.GetFinishGoodRecordSyncWithServer(_mesData, _mesData.ManufacturingOrder?.Name.ToString()).Result;
            var data = temp == null ? new List<IFinishGoodRecord>() : temp.ToList();
            e.Result = data;
        }


        private void btnSynchronize_Click(object sender, EventArgs e)
        {
            if (_syncWorker.IsBusy) return;
            if (_mesData == null) return;
            if (_mesData.ManufacturingOrder == null) return;
            lblLoading.Visible = true;
            _syncWorker.RunWorkerAsync();
        }

    
        private void kryptonDataGridView1_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_bindingList==null)return;
            kryptonDataGridView1.DataSource = _sortAscending ? _bindingList.OrderBy(kryptonDataGridView1.Columns[e.ColumnIndex].DataPropertyName).ToList() : _bindingList.OrderBy(kryptonDataGridView1.Columns[e.ColumnIndex].DataPropertyName).Reverse().ToList();
            _sortAscending = !_sortAscending;
        }

        private void btnSaveSetting_Click(object sender, EventArgs e)
        {
            _autoStandBy.SetStandByTimer((double)tbPreStandBy.Value, (double)tbPrePopUp.Value);
            _autoStandBy.SaveCurrentSetting(_standByConnection);
        }

        private void tmrAutoStandByChecker_Tick(object sender, EventArgs e)
        {
            tmrAutoStandByChecker.Stop();
            lbPreStandBy.Text = (_autoStandBy.AutoStandBySetting.PreStandByTimer - _autoStandBy.PreStandByTimer.ElapsedMilliseconds/1000f).ToString("0.#");
            if (_startStandByTimer == 1) { StartStandByTimer(); if (_startStandByTimer == 1) _startStandByTimer = 0; };
            if (_startStandByTimer == 2) { StopStandByTimer(); if(_autoStandBy.StandByState!=StandByState.PreStandByTimer) RestoreStatusPreStandBy(); if (_startStandByTimer ==2) _startStandByTimer = 0; } ;
            lbStandbyState.Text = _autoStandBy.StandByState.ToString("G");
            tmrAutoStandByChecker.Start();
        }
    }
}
