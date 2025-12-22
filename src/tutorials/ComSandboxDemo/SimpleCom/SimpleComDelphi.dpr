library SimpleComDelphi; // Must match filename SimpleComDelphi.dpr

{$MODE DELPHI}

uses
  Windows, ComObj, ActiveX, ComServ;

type
  // 1. Interface Definition
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall; 
  end;

  // 2. Implementation Class
  TSimpleCalculator = class(TComObject, ICalculator)
  protected
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

{ TSimpleCalculator Implementation }

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: WideString; stdcall;
begin
  // This allocates a BSTR that .NET will free
  Result := 'Running the native Delphi (FPC) COM object';
end;

// 3. The Factory Registration
initialization
  TComObjectFactory.Create(ComServer, TSimpleCalculator, 
    '{11111111-2222-3333-4444-555555555555}',
    'SimpleCalculator', '', ciMultiInstance, tmBoth);
end.