'------------------------------------------------------------------------------
' <auto-generated>
'     This code was generated from a template.
'
'     Manual changes to this file may cause unexpected behavior in your application.
'     Manual changes to this file will be overwritten if the code is regenerated.
' </auto-generated>
'------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic

Partial Public Class ProcessPaymentLine
    Public Property ID As System.Guid
    Public Property HeaderID As System.Guid
    Public Property AccountRef As String
    Public Property AmountOutstanding As Decimal
    Public Property TranNumber As String
    Public Property HeadNumber As String
    Public Property InvRef As String

    Public Overridable Property ProcessPaymentHeader As ProcessPaymentHeader

End Class
