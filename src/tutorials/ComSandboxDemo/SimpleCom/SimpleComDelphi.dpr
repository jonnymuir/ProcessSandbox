library SimpleComDelphi;

{$MODE DELPHI}

uses
  Windows, 
  ComObj, 
  ActiveX, 
  ComServ;

const
  // Match the CLSID and IID exactly to your C# and C versions
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';
  IID_ICalculator: TGUID        = '{E1234567-ABCD-1234-EF12-0123456789AB}';

type
  // 1. Interface Definition (Must match the VTable order in C#)
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall; 
  end;

  // 2. Implementation Class inheriting from TComObject
  // TComObject handles IUnknown (AddRef, Release, QueryInterface) automatically
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
  // In Delphi/FPC, WideString is binary-compatible with BSTR.
  // The memory will be freed by the caller (.NET)
  Result := 'Running the native Delphi (FPC) COM object';
end;

// 3. Factory Registration
// We use the simpler constructor to avoid the TThreadingModel type-mismatch error
initialization
  TComObjectFactory.Create(
    ComServer, 
    TSimpleCalculator, 
    CLSID_SimpleCalculator, 
    'SimpleCalculator', 
    'Simple Calculator COM Object'
  );
end.