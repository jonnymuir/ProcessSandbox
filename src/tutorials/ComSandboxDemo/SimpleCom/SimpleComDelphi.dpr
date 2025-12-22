unit SimpleCalculatorImpl;

{$MODE DELPHI}

interface

uses
  Windows, ComObj, ActiveX, ComServ;

type
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall; // Matches C# [PreserveSig]
  end;

  TSimpleCalculator = class(TComObject, ICalculator)
  protected
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

implementation

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: WideString; stdcall;
begin
  // Delphi's WideString is a BSTR. 
  // The caller (C#) will take ownership and free it.
  Result := 'Running the native Delphi (FPC) COM object';
end;

initialization
  TComObjectFactory.Create(ComServer, TSimpleCalculator, 
    '{11111111-2222-3333-4444-555555555555}',
    'SimpleCalculator', '', ciMultiInstance, tmBoth);
end.