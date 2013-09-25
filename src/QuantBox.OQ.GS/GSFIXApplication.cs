using System;
using System.Collections.Generic;

using SmartQuant.FIX;
using SmartQuant.FIXApplication;
using QuickFix;
using SmartQuant.Execution;
using SmartQuant.Instruments;
using SmartQuant;
using System.Collections;
using SmartQuant.Data;
using System.Runtime.InteropServices;
using System.Text;

namespace QuantBox.OQ.GS
{
    class GSFIXApplication : QuickFIX42CommonApplication
    {
        private const string OpenPrefix = "O|";
        private const string ClosePrefix = "C|";

        [DllImport("gsencrypt.dll", CallingConvention = CallingConvention.Cdecl,CharSet=CharSet.Ansi)]
        static extern int gsEncrypt(int pi_iMode, string pi_pszDataRaw, int pi_iDataRawSize, string pi_pszKey, byte[] po_pszDataEncrypt, int pi_iDataEncryptSize);

        // Class members
        private Hashtable cancelRequests = new Hashtable();


        public GSFIXApplication(GSFIX provider)
            : base(provider,"GS_FIX")
        {
        }

        public override void toAdmin(QuickFix.Message message, QuickFix.SessionID sessionID)
        {
            base.toAdmin(message, sessionID);

            if (message is QuickFix42.Logon)
            {
                message.setField(new ResetSeqNumFlag(true));

                GSFIX gsfix = provider as GSFIX;

                string encrypt_pwd = gsfix.Password;
                byte[] encrypt_pwd_byte = new byte[128];
                switch (gsfix.EncryptType)
                {
                    case EncryptType.NONE:
                        message.setField(new EncryptMethod(EncryptMethod.NONE));
                        break;
                    case EncryptType.DESECB:
                        gsEncrypt(2, gsfix.Password, gsfix.Password.Length, gsfix.PublicKey, encrypt_pwd_byte, 128);
                        encrypt_pwd = Encoding.ASCII.GetString(encrypt_pwd_byte);
                        message.setField(new EncryptMethod(EncryptMethod.DESECBMODE));
                        break;
                    case EncryptType.BlowFish:
                        gsEncrypt(101, gsfix.Password, gsfix.Password.Length, gsfix.PublicKey, encrypt_pwd_byte, 128);
                        encrypt_pwd = Encoding.ASCII.GetString(encrypt_pwd_byte);
                        message.setField(new EncryptMethod((int)EncryptType.BlowFish));
                        break;
                    default:
                        break;
                }

                if (!string.IsNullOrEmpty(gsfix.Account) && !string.IsNullOrEmpty(gsfix.CreditAccount))
                {
                    message.setField(new RawData(string.Format("T:{0},{1}:{2}:", gsfix.Account, gsfix.CreditAccount, encrypt_pwd.ToString())));
                }
                else if (!string.IsNullOrEmpty(gsfix.Account))
                {
                    message.setField(new RawData(string.Format("Z:{0}:{1}:", gsfix.Account, encrypt_pwd.ToString())));
                }
                else if (!string.IsNullOrEmpty(gsfix.CreditAccount))
                {
                    message.setField(new RawData(string.Format("X:{0}:{1}:", gsfix.CreditAccount, encrypt_pwd.ToString())));
                }
                else
                {
                    message.setField(new RawData(string.Format("T:{0},{1}:{2}:", gsfix.Account, gsfix.CreditAccount, encrypt_pwd.ToString())));
                }
            }
        }

        public override void fromAdmin(Message message, SessionID sessionID)
        {
            base.fromAdmin(message, sessionID);

            if ((message is QuickFix42.Logout || message is QuickFix42.Reject) && message.isSetField(QuickFix.Text.FIELD))
            {
                Console.WriteLine(message.getString(QuickFix.Text.FIELD));
            }

            if (message is QuickFix42.Logout)
            {
                Disconnect();
            }
        }

        public override void Disconnect()
        {
            base.Disconnect();

            // 父类的断开连接要求登录后才断开，实际上可能是密码错了
            Session session;

            // price
            if (priceSessionID != null)
            {
                session = Session.lookupSession(priceSessionID);

                if (session != null/* && session.isLoggedOn()*/)
                    session.logout();
            }

            // order
            if (orderSessionID != null)
            {
                session = Session.lookupSession(orderSessionID);

                if (session != null/* && session.isLoggedOn()*/)
                    session.logout();
            }
        }

        public override void onMessage(QuickFix42.BusinessMessageReject message, SessionID session)
        {
            string text = (message.isSetText()) ? message.getText().getValue() : null;

            if (text != null)
                provider.EmitError(text);
        }

        public override void onMessage(QuickFix42.MarketDataSnapshotFullRefresh snapshot, QuickFix.SessionID sessionID)
        {
            if (snapshot.isSetNoMDEntries())
            {
                string reqID = snapshot.getMDReqID().getValue();

                Instrument instrument = (provider as GSFIX).GetInstrument(reqID);

                instrument.OrderBook.Clear();

                QuickFix42.MarketDataSnapshotFullRefresh.NoMDEntries group = new QuickFix42.MarketDataSnapshotFullRefresh.NoMDEntries();

                Quote quote = new Quote();

                quote.DateTime = Clock.Now;

                for (uint i = 1; i <= snapshot.getNoMDEntries().getValue(); i++)
                {
                    snapshot.getGroup(i, group);

                    SmartQuant.Data.MarketDepth depth;

                    int position = 0;

                    if (group.isSetMDEntryPositionNo())
                        position = group.getMDEntryPositionNo().getValue() - 1;

                    double price = group.getMDEntryPx().getValue();
                    int size = (int)group.getMDEntrySize().getValue();

                    // Console.WriteLine("Snapshot Level : " + position + " " + price + " " + size);

                    switch (group.getMDEntryType().getValue())
                    {
                        case QuickFix.MDEntryType.TRADE:

                            provider.EmitTrade(new Trade(Clock.Now, price, size), instrument);

                            break;

                        case QuickFix.MDEntryType.BID:

                            // market depth

                            depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Insert, MDSide.Bid, price, size);

                            provider.EmitMarketDepth(depth, instrument);

                            // quote

                            if (position == 0)
                            {
                                quote.Bid = price;
                                quote.BidSize = size;
                            }

                            break;

                        case QuickFix.MDEntryType.OFFER:

                            // market depth

                            depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Insert, MDSide.Ask, price, size);

                            provider.EmitMarketDepth(depth, instrument);

                            // quote

                            if (position == 0)
                            {
                                quote.Ask = price;
                                quote.AskSize = size;
                            }

                            break;
                    }
                }

                group.Dispose();

                provider.EmitQuote(quote, instrument);
            }
        }

        public override void onMessage(QuickFix42.MarketDataIncrementalRefresh refresh, QuickFix.SessionID sessionID)
        {
            if (refresh.isSetNoMDEntries())
            {
                string reqID = refresh.getMDReqID().getValue();

                Instrument instrument = (provider as GSFIX).GetInstrument(reqID);

                if (instrument == null)
                    return;

                QuickFix42.MarketDataIncrementalRefresh.NoMDEntries group = new QuickFix42.MarketDataIncrementalRefresh.NoMDEntries();

                int position;
                double price;
                int size;

                SmartQuant.Data.MarketDepth depth;
                SmartQuant.Data.Quote quote;

                for (uint i = 1; i <= refresh.getNoMDEntries().getValue(); i++)
                {
                    refresh.getGroup(i, group);

                    switch (group.getMDUpdateAction().getValue())
                    {
                        // new

                        case QuickFix.MDUpdateAction.NEW:
                            {
                                switch (group.getMDEntryType().getValue())
                                {
                                    case QuickFix.MDEntryType.BID:

                                        //Console.WriteLine("NEW BID");

                                        price = group.getMDEntryPx().getValue();
                                        size = (int)group.getMDEntrySize().getValue();

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", -1, MDOperation.Insert, MDSide.Bid, price, size);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote, best bid

                                        if (price > instrument.Quote.Bid)
                                        {
                                            quote = new Quote(instrument.Quote);

                                            quote.DateTime = Clock.Now;
                                            quote.Bid = price;
                                            quote.BidSize = size;

                                            provider.EmitQuote(quote, instrument);
                                        }

                                        break;

                                    case QuickFix.MDEntryType.OFFER:

                                        //Console.WriteLine("NEW ASK");

                                        price = group.getMDEntryPx().getValue();
                                        size = (int)group.getMDEntrySize().getValue();

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", -1, MDOperation.Insert, MDSide.Ask, price, size);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote, best ask

                                        if (price < instrument.Quote.Ask)
                                        {
                                            quote = new Quote(instrument.Quote);

                                            quote.DateTime = Clock.Now;
                                            quote.Ask = price;
                                            quote.AskSize = size;

                                            provider.EmitQuote(quote, instrument);
                                        }

                                        break;

                                    case QuickFix.MDEntryType.TRADE:

                                        provider.EmitTrade(new Trade(Clock.Now, group.getMDEntryPx().getValue(), (int)group.getMDEntrySize().getValue()), instrument);

                                        break;
                                }
                            }
                            break;

                        // change

                        case QuickFix.MDUpdateAction.CHANGE:
                            {
                                switch (group.getMDEntryType().getValue())
                                {
                                    case QuickFix.MDEntryType.BID:

                                        //Console.WriteLine("CHANGE BID!");

                                        position = group.getMDEntryPositionNo().getValue() - 1;
                                        size = (int)group.getMDEntrySize().getValue();

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Update, MDSide.Bid, 0, size);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote, best bid

                                        if (position == 0)
                                        {
                                            quote = new Quote(instrument.Quote);

                                            quote.DateTime = Clock.Now;
                                            quote.BidSize = (int)group.getMDEntrySize().getValue();

                                            provider.EmitQuote(quote, instrument);
                                        }

                                        break;

                                    case QuickFix.MDEntryType.OFFER:

                                        //Console.WriteLine("CHANGE ASK!");

                                        position = group.getMDEntryPositionNo().getValue() - 1;
                                        size = (int)group.getMDEntrySize().getValue();

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Update, MDSide.Ask, 0, size);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote, best bid

                                        if (position == 0)
                                        {
                                            quote = new Quote(instrument.Quote);

                                            quote.DateTime = Clock.Now;
                                            quote.AskSize = (int)group.getMDEntrySize().getValue();

                                            provider.EmitQuote(quote, instrument);
                                        }

                                        break;
                                }
                            }
                            break;

                        // delete

                        case QuickFix.MDUpdateAction.DELETE:
                            {
                                switch (group.getMDEntryType().getValue())
                                {
                                    case QuickFix.MDEntryType.BID:

                                        //Console.WriteLine("DELETE BID");

                                        position = group.getMDEntryPositionNo().getValue() - 1;

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Delete, MDSide.Bid, 0, 0);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote

                                        if (position == 0)
                                        {
                                            Quote newQuote = instrument.OrderBook.GetQuote(0);

                                            newQuote.DateTime = Clock.Now;

                                            provider.EmitQuote(newQuote, instrument);
                                        }
                                        break;

                                    case QuickFix.MDEntryType.OFFER:

                                        //Console.WriteLine("DELETE ASK");

                                        position = group.getMDEntryPositionNo().getValue() - 1;

                                        // market depth

                                        depth = new SmartQuant.Data.MarketDepth(Clock.Now, "", position, MDOperation.Delete, MDSide.Ask, 0, 0);

                                        provider.EmitMarketDepth(depth, instrument);

                                        // quote

                                        if (position == 0)
                                        {
                                            Quote newQuote = instrument.OrderBook.GetQuote(0);

                                            newQuote.DateTime = Clock.Now;

                                            provider.EmitQuote(newQuote, instrument);
                                        }

                                        break;
                                }
                            }
                            break;
                    }
                }

                group.Dispose();
            }
        }

        public override void onMessage(QuickFix42.OrderCancelReject message, SessionID session)
        {
            OrderCancelReject reject = new OrderCancelReject();

            // required fields
            reject.TransactTime = Clock.Now;
            reject.OrderID = message.getOrderID().getValue();
            reject.ClOrdID = message.getClOrdID().getValue();
            reject.OrigClOrdID = message.getOrigClOrdID().getValue();

            (reject as FIXOrderCancelReject).OrdStatus = message.getOrdStatus().getValue();
            (reject as FIXOrderCancelReject).CxlRejResponseTo = message.getCxlRejResponseTo().getValue();
            (reject as FIXOrderCancelReject).CxlRejReason = message.getCxlRejReason().getValue();

            // optional fields
            if (message.isSetSecondaryOrderID())
                reject.SecondaryOrderID = message.getSecondaryOrderID().getValue();

            if (message.isSetAccount())
                reject.Account = message.getAccount().getValue();

            if (message.isSetText())
                reject.Text = message.getText().getValue();

            // event
            provider.EmitOrderCancelReject(reject);
        }

        public override void onMessage(QuickFix42.ExecutionReport report, QuickFix.SessionID sessionID)
        {
            if (report.getExecType().getValue() == QuickFix.ExecType.PENDING_CANCEL ||
                report.getExecType().getValue() == QuickFix.ExecType.CANCELED ||
                report.getExecType().getValue() == QuickFix.ExecType.PENDING_REPLACE ||
                report.getExecType().getValue() == QuickFix.ExecType.REPLACE)
            {
                object request = cancelRequests[report.getClOrdID().getValue()];

                if (request == null)
                    report.set(new OrigClOrdID(report.getClOrdID().getValue()));
                else
                {
                    if (request is FIXOrderCancelRequest)
                        report.set(new OrigClOrdID((request as FIXOrderCancelRequest).OrigClOrdID));

                    if (request is FIXOrderCancelReplaceRequest)
                        report.set(new OrigClOrdID((request as FIXOrderCancelReplaceRequest).OrigClOrdID));
                }
            }

            ExecutionReport Report = new ExecutionReport();

            if (report.isSetOrderID()) Report.OrderID = report.getOrderID().getValue();
            ////if (report.isSetSecondaryOrderID()) Report.SecondaryOrderID = report.getSecondaryOrderID().getValue();
            if (report.isSetClOrdID()) Report.ClOrdID = report.getClOrdID().getValue();
            if (report.isSetOrigClOrdID()) Report.OrigClOrdID = report.getOrigClOrdID().getValue();
            ////if (report.isSetListID()) Report.ListID = report.getListID().getValue();
            if (report.isSetExecID()) Report.ExecID = report.getExecID().getValue();
            ////if (report.isSetExecRefID()) Report.ExecRefID = report.getExecRefID().getValue();
            if (report.isSetExecType()) (Report as FIXExecutionReport).ExecType = report.getExecType().getValue();
            if (report.isSetOrdStatus()) (Report as FIXExecutionReport).OrdStatus = report.getOrdStatus().getValue();
            if (report.isSetOrdRejReason()) Report.OrdRejReason = report.getOrdRejReason().getValue();
            ////if (report.isSetExecRestatementReason()) Report.ExecRestatementReason = report.getExecRestatementReason().getValue();
            ////if (report.isSetAccount()) Report.Account = report.getAccount().getValue();
            ////if (report.isSetSettlmntTyp()) Report.SettlType = report.getSettlmntTyp().getValue();
            //if (report.isSetFutSettDate           ()) Report.FutSettDate            = report.getFutSettDate           ().getValue();
            if (report.isSetSymbol()) Report.Symbol = report.getSymbol().getValue();
            ////if (report.isSetSymbolSfx()) Report.SymbolSfx = report.getSymbolSfx().getValue();
            ////if (report.isSetSecurityID()) Report.SecurityID = report.getSecurityID().getValue();
            //if (report.isSetIDSource              ()) Report.IDSource               = report.getIDSource              ().getValue();
            ////if (report.isSetSecurityType()) Report.SecurityType = report.getSecurityType().getValue();
            ////if (report.isSetMaturityMonthYear()) Report.MaturityMonthYear = report.getMaturityMonthYear().getValue();
            //if (report.isSetMaturityDay           ()) Report.MaturityDate           = DateTime.Parse(report.getMaturityDay           ().getValue());
            //if (report.isSetPutOrCall             ()) Report.PutOrCall              = report.getPutOrCall             ().getValue();
            ////if (report.isSetStrikePrice()) Report.StrikePrice = report.getStrikePrice().getValue();
            ////if (report.isSetOptAttribute()) Report.OptAttribute = report.getOptAttribute().getValue();
            ////if (report.isSetContractMultiplier()) Report.ContractMultiplier = report.getContractMultiplier().getValue();
            ////if (report.isSetCouponRate()) Report.CouponRate = report.getCouponRate().getValue();
            ////if (report.isSetSecurityExchange()) Report.SecurityExchange = report.getSecurityExchange().getValue();
            ////if (report.isSetIssuer()) Report.Issuer = report.getIssuer().getValue();
            ////if (report.isSetEncodedIssuerLen()) Report.EncodedIssuerLen = report.getEncodedIssuerLen().getValue();
            ////if (report.isSetEncodedIssuer()) Report.EncodedIssuer = report.getEncodedIssuer().getValue();
            ////if (report.isSetSecurityDesc()) Report.SecurityDesc = report.getSecurityDesc().getValue();
            ////if (report.isSetEncodedSecurityDescLen()) Report.EncodedSecurityDescLen = report.getEncodedSecurityDescLen().getValue();
            ////if (report.isSetEncodedSecurityDesc()) Report.EncodedSecurityDesc = report.getEncodedSecurityDesc().getValue();
            if (report.isSetSide()) (Report as FIXExecutionReport).Side = report.getSide().getValue();
            if (report.isSetOrderQty()) Report.OrderQty = report.getOrderQty().getValue();
            ////if (report.isSetCashOrderQty()) Report.CashOrderQty = report.getCashOrderQty().getValue();
            if (report.isSetOrdType()) (Report as FIXExecutionReport).OrdType = report.getOrdType().getValue();
            if (report.isSetPrice()) Report.Price = report.getPrice().getValue();
            ////if (report.isSetStopPx()) Report.StopPx = report.getStopPx().getValue();
            //if (report.isSetPegDifference         ()) Report.PegDifference          = report.getPegDifference         ().getValue();		
            ////if (report.isSetDiscretionInst()) Report.DiscretionInst = report.getDiscretionInst().getValue();
            ////if (report.isSetDiscretionOffset()) Report.DiscretionOffsetValue = report.getDiscretionOffset().getValue();
            ////if (report.isSetCurrency()) Report.Currency = report.getCurrency().getValue();
            ////if (report.isSetComplianceID()) Report.ComplianceID = report.getComplianceID().getValue();
            //if (report.isSetSolicitedFlag         ()) Report.SolicitedFlag          = report.getSolicitedFlag         ().getValue();
            ////if (report.isSetTimeInForce()) (Report as FIXExecutionReport).TimeInForce = report.getTimeInForce().getValue();
            ////if (report.isSetEffectiveTime()) Report.EffectiveTime = report.getEffectiveTime().getValue();
            ////if (report.isSetExpireDate()) Report.ExpireDate = DateTime.Parse(report.getExpireDate().getValue());
            ////if (report.isSetExpireTime()) Report.ExpireTime = report.getExpireTime().getValue();
            ////if (report.isSetExecInst()) Report.ExecInst = report.getExecInst().getValue();
            //if (report.isSetRule80A               ()) Report.Rule80A                = report.getRule80A               ().getValue();
            if (report.isSetLastShares()) Report.LastQty = report.getLastShares().getValue();
            if (report.isSetLastPx()) Report.LastPx = report.getLastPx().getValue();
            ////if (report.isSetLastSpotRate()) Report.LastSpotRate = report.getLastSpotRate().getValue();
            ////if (report.isSetLastForwardPoints()) Report.LastForwardPoints = report.getLastForwardPoints().getValue();
            ////if (report.isSetLastMkt()) Report.LastMkt = report.getLastMkt().getValue();
            ////if (report.isSetTradingSessionID()) Report.TradingSessionID = report.getTradingSessionID().getValue();
            ////if (report.isSetLastCapacity()) Report.LastCapacity = report.getLastCapacity().getValue();
            if (report.isSetLeavesQty()) Report.LeavesQty = report.getLeavesQty().getValue();
            if (report.isSetCumQty()) Report.CumQty = report.getCumQty().getValue();
            if (report.isSetAvgPx()) Report.AvgPx = report.getAvgPx().getValue();
            ////if (report.isSetDayOrderQty()) Report.DayOrderQty = report.getDayOrderQty().getValue();
            ////if (report.isSetDayCumQty()) Report.DayCumQty = report.getDayCumQty().getValue();
            ////if (report.isSetDayAvgPx()) Report.DayAvgPx = report.getDayAvgPx().getValue();
            ////if (report.isSetGTBookingInst()) Report.GTBookingInst = report.getGTBookingInst().getValue();
            ////if (report.isSetTradeDate()) Report.TradeDate = DateTime.Parse(report.getTradeDate().getValue());
            if (report.isSetTransactTime()) Report.TransactTime = report.getTransactTime().getValue();
            //if (report.isSetReportToExch          ()) Report.ReportToExch           = report.getReportToExch          ().getValue();
            ////if (report.isSetCommission()) Report.Commission = report.getCommission().getValue();
            ////if (report.isSetCommType()) (Report as FIXExecutionReport).CommType = report.getCommType().getValue();
            ////if (report.isSetGrossTradeAmt()) Report.GrossTradeAmt = report.getGrossTradeAmt().getValue();
            ////if (report.isSetSettlCurrAmt()) Report.SettlCurrAmt = report.getSettlCurrAmt().getValue();
            ////if (report.isSetSettlCurrency()) Report.SettlCurrency = report.getSettlCurrency().getValue();
            ////if (report.isSetHandlInst()) Report.HandlInst = report.getHandlInst().getValue();
            ////if (report.isSetMinQty()) Report.MinQty = report.getMinQty().getValue();
            ////if (report.isSetMaxFloor()) Report.MaxFloor = report.getMaxFloor().getValue();
            //if (report.isSetOpenClose             ()) Report.OpenClose              = report.getOpenClose             ().getValue();
            ////if (report.isSetMaxShow()) Report.MaxShow = report.getMaxShow().getValue();
            if (report.isSetText()) Report.Text = report.getText().getValue();
            ////if (report.isSetEncodedTextLen()) Report.EncodedTextLen = report.getEncodedTextLen().getValue();
            ////if (report.isSetEncodedText()) Report.EncodedText = report.getEncodedText().getValue();
            //if (report.isSetFutSettDate2          ()) Report.FutSettDate2           = report.getFutSettDate2          ().getValue();
            ////if (report.isSetOrderQty2()) Report.OrderQty2 = report.getOrderQty2().getValue();
            //if (report.isSetClearingFirm          ()) Report.ClearingFirm           = report.getClearingFirm          ().getValue();
            //if (report.isSetClearingAccount       ()) Report.ClearingAccount        = report.getClearingAccount       ().getValue();
            ////if (report.isSetMultiLegReportingType()) Report.MultiLegReportingType = report.getMultiLegReportingType().getValue();

            //

            SingleOrder order;

            if (Report.ExecType == SmartQuant.FIX.ExecType.PendingCancel ||
                Report.ExecType == SmartQuant.FIX.ExecType.Cancelled ||
                Report.ExecType == SmartQuant.FIX.ExecType.PendingReplace ||
                Report.ExecType == SmartQuant.FIX.ExecType.Replace)

                order = OrderManager.Orders.All[Report.OrigClOrdID] as SingleOrder;
            else
                order = OrderManager.Orders.All[Report.ClOrdID] as SingleOrder;

            Instrument instrument = order.Instrument;

            Report.Symbol = instrument.Symbol;

            Report.TransactTime = Clock.Now;

            // emit execution report

            EmitExecutionReport(Report);
        }

        public override void Send(FIXMarketDataRequest Request)
        {
            //Console.WriteLine("REQUEST");

            QuickFix42.MarketDataRequest request = new QuickFix42.MarketDataRequest(
                new QuickFix.MDReqID(Request.MDReqID),
                new QuickFix.SubscriptionRequestType(SubscriptionRequestType.SNAPSHOT), //GS FIX
                new QuickFix.MarketDepth(5)); //GS FIX

            //request.set(new QuickFix.MDUpdateType(Request.MDUpdateType));
            //request.set(new QuickFix.AggregatedBook(Request.AggregatedBook));

            QuickFix42.MarketDataRequest.NoMDEntryTypes typeGroup;

            typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            typeGroup.set(new MDEntryType(FIXMDEntryType.Bid));
            request.addGroup(typeGroup);
            typeGroup.Dispose();

            typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            typeGroup.set(new MDEntryType(FIXMDEntryType.Offer));
            request.addGroup(typeGroup);
            typeGroup.Dispose();

            typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            typeGroup.set(new MDEntryType(FIXMDEntryType.Trade));
            request.addGroup(typeGroup);
            typeGroup.Dispose();

            //typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            //typeGroup.set(new MDEntryType(FIXMDEntryType.Open));
            //request.addGroup(typeGroup);
            //typeGroup.Dispose();

            //typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            //typeGroup.set(new MDEntryType(FIXMDEntryType.Close));
            //request.addGroup(typeGroup);
            //typeGroup.Dispose();

            //typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            //typeGroup.set(new MDEntryType(FIXMDEntryType.High));
            //request.addGroup(typeGroup);
            //typeGroup.Dispose();

            //typeGroup = new QuickFix42.MarketDataRequest.NoMDEntryTypes();
            //typeGroup.set(new MDEntryType(FIXMDEntryType.Low));
            //request.addGroup(typeGroup);
            //typeGroup.Dispose();

            QuickFix42.MarketDataRequest.NoRelatedSym symGroup = new QuickFix42.MarketDataRequest.NoRelatedSym();

            FIXRelatedSymGroup Group = Request.GetRelatedSymGroup(0);

            symGroup.set(new QuickFix.Symbol(Group.Symbol.Substring(0, 6)));

            if (Group.Symbol.StartsWith("60") || Group.Symbol.StartsWith("51"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHG"));// 上海
                symGroup.set(new QuickFix.SecurityExchange("SS"));// 上海
            }
            else if (Group.Symbol.StartsWith("00") || Group.Symbol.StartsWith("30") || Group.Symbol.StartsWith("15"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHE"));// 深圳
                symGroup.set(new QuickFix.SecurityExchange("SZ"));// 深圳
            }

            //if (Group.ContainsField(EFIXField.SecurityType)) symGroup.set(new QuickFix.SecurityType(Group.SecurityType));
            //if (Group.ContainsField(EFIXField.MaturityMonthYear)) symGroup.set(new QuickFix.MaturityMonthYear(Group.MaturityMonthYear));
            //if (Group.ContainsField(EFIXField.MaturityDay           )) symGroup.set(new QuickFix.MaturityDay           (Group.MaturityDay           ));
            //if (Group.ContainsField(EFIXField.PutOrCall             )) symGroup.set(new QuickFix.PutOrCall             (Group.PutOrCall             ));
            //if (Group.ContainsField(EFIXField.StrikePrice)) symGroup.set(new QuickFix.StrikePrice(Group.StrikePrice));
            //if (Group.ContainsField(EFIXField.OptAttribute)) symGroup.set(new QuickFix.OptAttribute(Group.OptAttribute));
            //if (Group.ContainsField(EFIXField.SecurityID)) symGroup.set(new QuickFix.SecurityID(Group.SecurityID));
            //if (Group.ContainsField(EFIXField.SecurityExchange)) symGroup.set(new QuickFix.SecurityExchange(Group.SecurityExchange));

            request.addGroup(symGroup);

            symGroup.Dispose();

            if(base.priceSessionID == null)
                Session.sendToTarget(request, orderSessionID);
            else
                Session.sendToTarget(request, priceSessionID);
        }

        public override void Send(FIXNewOrderSingle Order)
        {
            QuickFix42.NewOrderSingle order = new QuickFix42.NewOrderSingle(
                new QuickFix.ClOrdID(Order.ClOrdID),
                //new QuickFix.HandlInst(Order.HandlInst),
                new QuickFix.HandlInst(HandlInst.AUTOEXECPRIV), // GS FIX
                new QuickFix.Symbol(Order.Symbol.Substring(0,6)),
                new QuickFix.Side(Order.Side),
                new QuickFix.TransactTime(Order.TransactTime),
                new QuickFix.OrdType(Order.OrdType));

            if ((Order.OrdType == FIXOrdType.Limit || Order.OrdType == FIXOrdType.StopLimit) && Order.ContainsField(EFIXField.Price))
                order.set(new QuickFix.Price(Order.Price));

            if ((Order.OrdType == FIXOrdType.Stop || Order.OrdType == FIXOrdType.StopLimit) && Order.ContainsField(EFIXField.StopPx))
                order.set(new QuickFix.StopPx(Order.StopPx));

            // 自己计算交易所
            if (Order.Symbol.StartsWith("60") || Order.Symbol.StartsWith("51"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHG"));// 上海
                order.set(new QuickFix.SecurityExchange("SS"));// 上海
            }
            else if (Order.Symbol.StartsWith("00") || Order.Symbol.StartsWith("30") || Order.Symbol.StartsWith("15"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHE"));// 深圳
                order.set(new QuickFix.SecurityExchange("SZ"));// 深圳
            }

            if (Order.Text.StartsWith(OpenPrefix))
            {
                order.setField(new QuickFix.CashMargin(CashMargin.MARGIN_OPEN));
            }
            else if (Order.Text.StartsWith(ClosePrefix))
            {
                order.setField(new QuickFix.CashMargin(CashMargin.MARGIN_CLOSE));
            }

            //order.set(new QuickFix.SecurityType(Order.SecurityType));
            order.set(new QuickFix.OrderQty(Order.OrderQty));
            //order.set(new QuickFix.Account(Order.Account));
            //order.set(new QuickFix.Rule80A(Order.Rule80A));
            //order.set(new QuickFix.CustomerOrFirm(Order.CustomerOrFirm));

            //if (Order.ContainsField(EFIXField.ClearingAccount))
            //    order.set(new QuickFix.ClearingAccount(Order.ClearingAccount));

            order.set(new QuickFix.Currency("CNY")); // GS FIX

            Session.sendToTarget(order, orderSessionID);
        }

        public override void Send(FIXOrderCancelRequest Request)
        {
            QuickFix42.OrderCancelRequest request = new QuickFix42.OrderCancelRequest(
                new QuickFix.OrigClOrdID(Request.OrigClOrdID),
                new QuickFix.ClOrdID(Request.ClOrdID),
                new QuickFix.Symbol(Request.Symbol),
                new QuickFix.Side(Request.Side),
                new QuickFix.TransactTime(Request.TransactTime));

            // instrument component block

            //request.set(new QuickFix.SecurityType(Request.SecurityType));
            //request.set(new QuickFix.SecurityID(Request.SecurityID));
            //request.set(new QuickFix.SecurityExchange(Request.SecurityExchange));
            //request.set(new QuickFix.Account(Request.Account));

            request.set(new QuickFix.OrderQty(0)); //GS FIX

            cancelRequests.Add(Request.ClOrdID, Request);

            Session.sendToTarget(request, orderSessionID);
        }
    }
}
