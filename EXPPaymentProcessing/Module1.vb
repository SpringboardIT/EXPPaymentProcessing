Imports System.Globalization
Imports SOPAWorkflowLibrary

Module Module1

    Dim SageDataPath As String = ""
    Dim SageUsername As String = ""
    Dim SagePassword As String = ""
    Dim PaymentDirectory As String = ""
    Sub Main(ByVal args As String())
        Dim dtRunDate = DateTime.MinValue
        Dim liMonth As Integer = 0
        Dim iDay As Integer = 1
        If args.Count() > 1 Then
            For i As Integer = 0 To args.Count() - 1 Step 2
                Dim lsCommand As String = args(i)
                Dim lsData As String = args(i + 1)
                Select Case lsCommand.ToUpper()
                    Case "-DAY"

                        Integer.TryParse(lsData, iDay)

                    Case "-MONTH"
                        Integer.TryParse(lsData, liMonth)
                    Case "-SAGEPATH"
                        SageDataPath = lsData
                    Case "-SAGEUSERNAME"
                        SageUsername = lsData
                    Case "-SAGEPASSWORD"
                        SagePassword = lsData
                    Case "-PAYMENTDIRECTORY"
                        My.Settings.BankUpload = lsData
                        My.Settings.Save()
                End Select
            Next i


            dtRunDate = New DateTime(DateTime.Now.AddMonths(liMonth).Year, DateTime.Now.AddMonths(liMonth).Month, iDay)
            Console.WriteLine("Running for Payments: " + dtRunDate)
        End If
        ProcessOutstandingPayments(dtRunDate)
    End Sub
    Public Sub ProcessOutstandingPayments(ByVal RunDate As DateTime)
        Using context As New ESSageSyncExampleEntities
            Dim lstPayments As List(Of ProcessPaymentHeader) = context.ProcessPaymentHeaders.Where(Function(x) x.Processed = False And x.Date <= RunDate).ToList() 'should i be trapping if it errors so doesnt process more than once
            Console.WriteLine("Got " + lstPayments.Count + " to run")
            If (Not IsNothing(lstPayments)) Then
                For Each payment As ProcessPaymentHeader In lstPayments
                    ProcessBankPayments(payment, True, True, False, context)
                    payment.Processed = True
                Next
            End If
            context.SaveChanges()
        End Using
    End Sub

    Public Sub ProcessBankPayments(ByVal paymentHeader As ProcessPaymentHeader, ByVal GenerateFile As Boolean, ByVal UpdateSage As Boolean, ByVal SageGenerateChequeNumbers As Boolean, ByVal context As ESSageSyncExampleEntities)
        'Dim sRead As New System.IO.StringReader(InvoiceDetails)
        'Dim tfp As New Microsoft.VisualBasic.FileIO.TextFieldParser(sRead)
        'tfp.SetDelimiters(",")
        ' For each invoice on the email process
        'create list of invoices from orbis file
        Dim _invoicelist As New Generic.SortedList(Of String, InvoiceDetail)
        Dim _chkinvoicelist As New Generic.List(Of InvoiceDetail)
        Dim InvoiceList As New InvoiceDetail
        Dim currentRow As String()

        Dim _paymentlist As New Generic.List(Of PaymentDetail)
        Dim PaymentList As New PaymentDetail
        Dim liTranNumber As Integer
        Dim liHeadNumber As Integer
        Dim lsAccount As String
        Dim ldAmountOutstanding As Double
        Dim lsCurrency As String = ""
        Dim lsCompanyCode As String = String.Empty
        Dim ldPaymentDate As Date = Now.Date
        Dim lbAddedInvoice As Boolean = False


        Dim lstLines As List(Of ProcessPaymentLine) = context.ProcessPaymentLines.Where(Function(x) x.HeaderID = paymentHeader.ID).ToList()


        'While Not tfp.EndOfData
        'Try
        'currentRow = tfp.ReadFields()
        'If currentRow.GetUpperBound(0) = 2 Then
        lsCurrency = paymentHeader.Currency
        'If Not Date.TryParse(currentRow(1), ldPaymentDate) Then
        '    Console.WriteLine("Cannot identify payment date")
        '    Exit Sub
        'End If
        ldPaymentDate = paymentHeader.Date
        lsCompanyCode = paymentHeader.Company
        Console.WriteLine("Processing payments for " & lsCompanyCode & " currency " & lsCurrency & " Payment Date " & ldPaymentDate.ToShortDateString)
        'End If


        For Each line As ProcessPaymentLine In lstLines
            Try
                Dim x As New InvoiceDetail
                If line.TranNumber <> 0 Then
                    x.Account = line.AccountRef
                    x.TranNumber = line.TranNumber
                    x.HeadNumber = line.HeadNumber
                    x.AmountOutstanding = line.AmountOutstanding
                    _invoicelist.Add(x.Account + Str(_invoicelist.Count + 1), x)
                End If
            Catch ex As Exception
                Console.WriteLine("Line " & ex.Message & "is not valid and will be skipped.")
            End Try
        Next

        ' End While
        If _invoicelist.Count = 0 Then
            Console.WriteLine("No invoices to process")
            ' error message and exit
            Exit Sub
        End If
        If String.IsNullOrEmpty(lsCompanyCode) Then
            Console.WriteLine("Company code not set")
            Exit Sub
        End If
        'Dim _helper As New SOPADatabaseHelper.OrderDataHelper(DataConnectionString)
        'Dim srow As SOPADatabaseHelper.dsOrderDatabase.SOPASageCompanyRow = _helper.GetSageCompanyRecord(lsCompanyCode)
        'If IsNothing(srow) Then
        ' Console.WriteLine("Invalid Sage company code " & lsCompanyCode)
        ' Exit Sub
        ' End If
        'If srow.IsDataDirectoryNull Or srow.IsPasswordNull Or srow.IsUsernameNull Then
        ' Console.WriteLine("Missing Sage data for company code " & lsCompanyCode)
        ' Exit Sub
        ' End If
        Dim AccObj As SITAccountPosting.ISITAccountPosting = Nothing
        AccObj = New SITAccountsInterface.SITAccountMain
        AccObj.NewInstance(lsCompanyCode, SageDataPath, SageUsername, SagePassword)
        AccObj.Connect() 'Chris Added 2/01/2019
        Console.WriteLine("Connected to Sage")
        ' get bank info of expedite
        Dim exaddress As SITAccountPosting.CompanyBankDetail
        exaddress = FindBankDetailsForCurrency(lsCurrency, lsCompanyCode, AccObj)

        If IsNothing(exaddress) Then
            AccObj.Disconnect()
            AccObj = Nothing
            Exit Sub
        End If

        ldAmountOutstanding = 0
        lsAccount = ""

        ' Generate lists of payments for file processing and for sage updating - all at one go
        Dim chkinvoice As InvoiceDetail = Nothing
        Dim payList As New Generic.List(Of SITAccountPosting.PurchasePaymentHeader)
        Dim payHeader As SITAccountPosting.PurchasePaymentHeader = Nothing
        For Each x As Generic.KeyValuePair(Of String, InvoiceDetail) In _invoicelist
            If Not String.IsNullOrEmpty(x.Value.Account) Then
                If x.Value.Account <> lsAccount Then
                    payHeader = New SITAccountPosting.PurchasePaymentHeader
                    payHeader.PaymentDate = ldPaymentDate
                    payHeader.BankNominal = exaddress.BankNominal
                    payHeader.SupplierAccount = x.Value.Account
                    payHeader.Description = "Payment"
                    payHeader.PaymentNumber = "SOPA"
                    payHeader.UserName = "SOPA"
                    payHeader.Currency = lsCurrency
                    payList.Add(payHeader)
                    chkinvoice = New InvoiceDetail
                    chkinvoice.Account = x.Value.Account
                    chkinvoice.HeadNumber = x.Value.HeadNumber
                    chkinvoice.InvoiceNumber = x.Value.TranNumber
                    _chkinvoicelist.Add(chkinvoice)
                    lsAccount = x.Value.Account
                End If
                If Not IsNothing(chkinvoice) Then
                    chkinvoice.AmountOutstanding = chkinvoice.AmountOutstanding + x.Value.AmountOutstanding
                End If
                If Not IsNothing(payHeader) Then
                    payHeader.Amount = payHeader.Amount + x.Value.AmountOutstanding
                    Dim pl As New SITAccountPosting.PurchasePaymentLine
                    pl.AccountingId = x.Value.HeadNumber
                    pl.Amount = x.Value.AmountOutstanding
                    payHeader.Lines.Add(pl)
                End If
            End If
        Next

        If GenerateFile Then
            Dim liBankLayout As SopaBankLayouts = GetPaymentFileLayoutEnum(exaddress.BankLayout)
            If liBankLayout = SopaBankLayouts.Invalid Then
                Console.WriteLine("Invalid layout code " & exaddress.BankLayout)
            ElseIf liBankLayout = SopaBankLayouts.NoCreateFile Then
                Console.WriteLine("Specified that no file to be created for this currency")
            Else
                For Each x As InvoiceDetail In _chkinvoicelist

                    Dim adpaymentDetail As New PaymentDetail
                    Dim lbValid As Boolean = False

                    adpaymentDetail.SupplierBankDetails = AccObj.GetSupplierBankDetails(x.Account)

                    lbValid = ValidateBankInfo(adpaymentDetail, liBankLayout, x.AmountOutstanding, x.Account)

                    If x.AmountOutstanding <= 0 Then
                        lbValid = False
                    End If
                    If lbValid Then
                        adpaymentDetail.Amount = x.AmountOutstanding
                        _paymentlist.Add(adpaymentDetail)
                    End If
                Next

                ' create Bacs
                'select case 
                Select Case liBankLayout
                    Case Is = SopaBankLayouts.HSBCUKBacs
                        WriteBacs(lsCompanyCode, lsCurrency, exaddress, _paymentlist, ldPaymentDate)
                    Case Is = SopaBankLayouts.HSBCEurozone
                        WriteSwift(lsCompanyCode, lsCurrency, _paymentlist, exaddress, ldPaymentDate, liBankLayout)
                    Case Is = SopaBankLayouts.HSBCPriority
                        WriteSwift(lsCompanyCode, lsCurrency, _paymentlist, exaddress, ldPaymentDate, liBankLayout)
                End Select

            End If
        End If

        Try
            If UpdateSage Then
                For Each x As SITAccountPosting.PurchasePaymentHeader In payList
                    AccObj.CreatePurchasePayment(x)
                Next
            End If
        Catch ex As Exception
            Console.WriteLine("Error creating purchase payments")
            Console.WriteLine(ex.Message)
            Console.WriteLine(ex.StackTrace)
        End Try

        For Each s As String In AccObj.AuditText(SITAccountPosting.AuditTextLevel.All)
            Console.WriteLine(s)
        Next
        AccObj.ClearAuditText()
        AccObj.Disconnect() 'removes connection and starts new connection on next link? Chris 2/1/19
        AccObj = Nothing


    End Sub
    Private Function GetCorrectLength(ByVal lsString As String, ByVal LengthRequired As Integer) As String
        If lsString.Length <= LengthRequired Then
            Return lsString
        Else
            Return lsString.Substring(0, LengthRequired)
        End If
    End Function
    Private Sub WriteText(ByVal lsLine As String, ByVal filename As String, ByVal append As Boolean)
        'My.Computer.FileSystem.WriteAllText(filename, lsLine & vbCrLf, True)
        My.Computer.FileSystem.WriteAllText(filename, lsLine & vbCrLf, True, System.Text.Encoding.Default)



    End Sub
    Public Function WriteSwift(CompanyCode As String, ByVal CurrencyCode As String, ByVal accdet As Generic.List(Of PaymentDetail), ByVal exaddress As SITAccountPosting.CompanyBankDetail, ByVal PaymentDate As Date, ByVal LayoutEnum As SopaBankLayouts) As Boolean
        'Const cSwiftDateFormat As String = "{0:ddMMyy}"
        Const cSwiftDateFormat As String = "{0:yyMMdd}"
        Dim DataFile As String = My.Settings.BankUpload & CompanyCode & "_" & CurrencyCode & String.Format("{0:s}", Now).Replace(":", "-") & ".txt"
        Select Case LayoutEnum
            Case Is = SopaBankLayouts.HSBCEurozone
                DataFile = My.Settings.BankUpload & "EZ_" & CompanyCode & "_" & CurrencyCode & String.Format("{0:s}", Now).Replace(":", "-") & ".txt"
            Case Is = SopaBankLayouts.HSBCPriority
                DataFile = My.Settings.BankUpload & "PP_" & CompanyCode & "_" & CurrencyCode & String.Format("{0:s}", Now).Replace(":", "-") & ".txt"
        End Select

        Dim lsLine As String = ""
        Dim lbFirst As Boolean = True
        'For Each x As Object In _accountlist.Item()
        For Each x As PaymentDetail In accdet
            If lbFirst Then
                'lsLine = ":20:" & GetCorrectLength(exaddress.BankName & String.Format(cSwiftDateFormat, PaymentDate), 16)
                lsLine = ":20:" & GetCorrectLength(x.SupplierBankDetails.BeneName & String.Format(cSwiftDateFormat, PaymentDate), 16)
                lbFirst = False
            Else
                lsLine = "-:20:" & GetCorrectLength(x.SupplierBankDetails.BeneName & String.Format(cSwiftDateFormat, PaymentDate) & accdet.Count, 16)
                'lsLine = "-:20:" & GetCorrectLength(exaddress.BankName & String.Format(cSwiftDateFormat, PaymentDate) & accdet.Count, 16)
            End If
            WriteText(lsLine, DataFile, True)
            lsLine = ":23B:" & "CRED"
            WriteText(lsLine, DataFile, True)
            Dim myCIintl As New CultureInfo("de-DE", False)
            lsLine = ":32A:" & String.Format(cSwiftDateFormat, PaymentDate) & CurrencyCode & x.Amount.ToString("###0.00", myCIintl)
            WriteText(lsLine, DataFile, True)
            'lsLine = ":33B:" & CurrencyCode & (x.Amount.ToString(myCIintl))
            'WriteText(lsLine, DataFile, True)
            'lsLine = ":50K:/" & GetCorrectLength(exaddress.AccountNumber, 34)
            'WriteText(lsLine, DataFile, True)

            'lsLine = ":50K:/" & GetCorrectLength(exaddress.AccountNumber, 34)

            'String.Format(exaddress.Sortcode)..Remove(-)
            lsLine = ":50K:/" & GetCorrectLength(exaddress.Sortcode.Replace("-", ""), 6) & GetCorrectLength(exaddress.AccountNumber, 28)
            WriteText(lsLine, DataFile, True)

            'lsLine = GetCorrectLength(exaddress.Reference1, 34)
            lsLine = GetCorrectLength(exaddress.BankName, 34)
            WriteText(lsLine, DataFile, True)

            'For c As Integer = 0 To GetMaxLine(exaddress.Address, 3)
            '    If Not String.IsNullOrEmpty(exaddress.Address(c)) Then
            '        lsLine = exaddress.Address(c)
            '        WriteText(lsLine, DataFile, True)
            '    End If
            'Next

            lsLine = ":57A:" & GetCorrectLength(x.SupplierBankDetails.BankBICSWIFT, 34)
            WriteText(lsLine, DataFile, True)
            lsLine = ":59:/" & GetCorrectLength(x.SupplierBankDetails.BankIban, 34)
            WriteText(lsLine, DataFile, True)
            lsLine = GetCorrectLength(x.SupplierBankDetails.BeneName, 34)
            WriteText(lsLine, DataFile, True)
            Select Case LayoutEnum
                Case Is = SopaBankLayouts.HSBCEurozone
                    lsLine = GetCorrectLength(x.SupplierBankDetails.Address(0), 34)
                    WriteText(lsLine, DataFile, True)
            End Select
            'If accdet.Address.Count <> 0 Then
            'End If
            'lsLine = GetCorrectLength(x.SupplierBankDetails.BankName, 34)
            'WriteText(lsLine, DataFile, True)
            'For i As Integer = 0 To GetMaxLine(x.SupplierBankDetails.Address, 3)
            '    If Not String.IsNullOrEmpty(x.SupplierBankDetails.Address(i)) Then
            '        lsLine = x.SupplierBankDetails.Address(i)
            '        WriteText(lsLine, DataFile, True)
            '    End If
            'Next
            lsLine = ":70:" & GetCorrectLength(x.SupplierBankDetails.Reference1, 35)
            WriteText(lsLine, DataFile, True)
            lsLine = ":71A:" & "SHA"
            WriteText(lsLine, DataFile, True)
            Select Case LayoutEnum
                Case Is = SopaBankLayouts.HSBCEurozone
                    lsLine = ":72:/REC/EZONE"
                Case Is = SopaBankLayouts.HSBCPriority
                    lsLine = ":72:/REC/LCC-" & x.SupplierBankDetails.BankBICSWIFT.Substring(4, 2)
            End Select
            WriteText(lsLine, DataFile, True)
        Next
    End Function
    Public Function WriteBacs(CompanyCode As String, ByVal CurrencyCode As String, ByVal Account As SITAccountPosting.CompanyBankDetail, ByVal accdet As Generic.List(Of PaymentDetail), ByVal PaymentDate As Date) As Boolean
        Dim DataFile As String = My.Settings.BankUpload & CompanyCode & "_" & CurrencyCode & String.Format("{0:s}", Now).Replace(":", "-") & ".txt"
        Const csAmountFormat As String = "{0:#####0.00}"
        Dim lsLine As String = ""
        Dim lbFirst As Boolean = True
        Dim ldTotal As Double = 0
        For Each x As PaymentDetail In accdet
            ldTotal = ldTotal + x.Amount
        Next
        '' get debit bank details
        If lbFirst Then
            lsLine = "$FN:" & 1
            WriteText(lsLine, DataFile, False)
            lsLine = "$FT:" & String.Format(csAmountFormat, ldTotal)
            WriteText(lsLine, DataFile, True)
            lsLine = "$FC:" & CurrencyCode
            WriteText(lsLine, DataFile, True)
            lsLine = "$DS"
            WriteText(lsLine, DataFile, True)
            lsLine = "F01:" & GetCorrectLength(Account.AccountNumber, 8)
            WriteText(lsLine, DataFile, True)
            lsLine = "F02:" & String.Format("{0:ddMMyy}", PaymentDate)
            WriteText(lsLine, DataFile, True)
            lsLine = "F03:" & GetCorrectLength(Account.BankName, 10)
            WriteText(lsLine, DataFile, True)
            lsLine = "F04:" & accdet.Count
            WriteText(lsLine, DataFile, True)
            lsLine = "F05:" & String.Format(csAmountFormat, ldTotal)
            WriteText(lsLine, DataFile, True)
        End If
        'For Each x As Object In _accountlist.Item()
        For Each x As PaymentDetail In accdet
            lsLine = "$LS"
            WriteText(lsLine, DataFile, True)
            lsLine = "S01:" & GetCorrectLength(x.SupplierBankDetails.AccountNumber, 8)
            WriteText(lsLine, DataFile, True)
            lsLine = "S02:" & GetCorrectLength(x.SupplierBankDetails.Sortcode, 6)
            WriteText(lsLine, DataFile, True)
            lsLine = "S03:" & GetCorrectLength(x.SupplierBankDetails.BeneName, 18)
            WriteText(lsLine, DataFile, True)
            lsLine = "S04:" & String.Format(csAmountFormat, x.Amount)
            WriteText(lsLine, DataFile, True)
            lsLine = "S05:" & GetCorrectLength(x.SupplierBankDetails.Reference1, 18)
            WriteText(lsLine, DataFile, True)
            lsLine = "$LE"
            WriteText(lsLine, DataFile, True)
        Next
        lsLine = "$DE"
        WriteText(lsLine, DataFile, True)
    End Function
    Private Function GetPaymentFileLayoutEnum(ByVal Layout As String) As SopaBankLayouts
        Select Case Layout
            Case Is = "HSBC UK BACS"
                Return SopaBankLayouts.HSBCUKBacs
            Case Is = "HSBC Eurozone"
                Return SopaBankLayouts.HSBCEurozone
            Case Is = "HSBC Priority Payments"
                Return SopaBankLayouts.HSBCPriority
            Case Is = "No File Creation"
                Return SopaBankLayouts.NoCreateFile
        End Select
        Return SopaBankLayouts.Invalid
    End Function
    Public Function ValidateBankInfo(ByRef BankDetail As SOPAWorkflowLibrary.PaymentDetail, ByVal LayoutEnum As SopaBankLayouts, ByVal amount As Double, ByVal accountref As String) As Boolean

        'If lbError = False Then
        Select Case LayoutEnum
            Case Is = SopaBankLayouts.HSBCUKBacs
                If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.AccountNumber) Then
                    Console.WriteLine("Account " & accountref & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                    'Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                    Return False
                End If
                If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.Sortcode) Then
                    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank Sort Code not found" & amount.ToString("C2"))
                    Return False
                End If
                BankDetail.SupplierBankDetails.Sortcode = BankDetail.SupplierBankDetails.Sortcode.Replace("-", String.Empty)
            Case Is = SopaBankLayouts.HSBCEurozone
                'If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.AccountNumber) Then
                '    Console.WriteLine("Account " & accountref & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                '    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                '    Return False
                'End If
                If BankDetail.SupplierBankDetails.Address.Count <= 0 Then
                    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Address not found")
                    Return False
                End If
                If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.BankBICSWIFT) Then
                    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank BICSWIFT Address not Found")
                    Return False
                End If
            Case Is = SopaBankLayouts.HSBCPriority
                If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.AccountNumber) Then
                    Console.WriteLine("Account " & accountref & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                    'Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank Account number not Found, Outstanding " & amount.ToString("C2"))
                    Return False
                End If
                If BankDetail.SupplierBankDetails.Address.Count <= 0 Then
                    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Address not found")
                    Return False
                End If
                If String.IsNullOrEmpty(BankDetail.SupplierBankDetails.BankBICSWIFT) Then
                    Console.WriteLine("Account " & BankDetail.SupplierBankDetails.AccountNumber & " Bank BICSWIFT Address not Found")
                    Return False
                End If
            Case Else
                Return False
        End Select
        Return True
    End Function
    Public Function FindBankDetailsForCurrency(ByVal CurrencyCode As String, CompanyCode As String, ByVal AccObj As SITAccountPosting.ISITAccountPosting) As SITAccountPosting.CompanyBankDetail
        Dim BankNom As BankNominal
        BankNom = GetBankNominal(CurrencyCode, CompanyCode)
        If IsNothing(BankNom) Then
            Console.WriteLine("Cannot find processing definition for currency " & CurrencyCode)
            Return Nothing
        End If
        Dim CoBank As SITAccountPosting.CompanyBankDetail = AccObj.GetCompanyBankDetails(BankNom.NominalCode)
        For Each s As String In AccObj.AuditText(SITAccountPosting.AuditTextLevel.All)
            Console.WriteLine(s)
        Next
        If Not IsNothing(CoBank) Then
            CoBank.BankLayout = BankNom.Layout
        End If
        AccObj.ClearAuditText()
        Return CoBank
    End Function
    Public Function GetBankNominal(ByVal CurrencyCode As String, CompanyCode As String) As BankNominal
        '    Public Shared Function GetBankNominal(ByVal CurrencyCode As String) As String
        Using context As New ESSageSyncExampleEntities
            Dim bn As BankNominal = context.BankNominals.Where(Function(x) x.CurrencyCode = CurrencyCode And x.SageCompanyCode = CompanyCode).FirstOrDefault()
            If bn Is Nothing Then
                'Return ""
                Return Nothing
            Else
                If String.IsNullOrEmpty(bn.NominalCode) Then
                    Return Nothing
                Else
                    Return bn
                End If
            End If
        End Using
    End Function
End Module