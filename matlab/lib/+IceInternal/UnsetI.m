%{
**********************************************************************

Copyright (c) 2003-2017 ZeroC, Inc. All rights reserved.

This copy of Ice is licensed to you under the terms described in the
ICE_LICENSE file included in this distribution.

**********************************************************************
%}

%
% Singleton used to indicate an unset optional value.
%
classdef UnsetI < handle
    methods(Access=private)
        function obj = UnsetI()
        end
    end
    methods
        function r = eq(obj, other)
            r = isequal(obj, other);
        end
        function r = ne(obj, other)
            r = ~isequal(obj, other);
        end
    end
    methods(Static)
        function obj = getInstance()
            persistent singleton;
            if isempty(singleton) || ~isvalid(singleton)
                singleton = IceInternal.UnsetI();
            end
            obj = singleton;
        end
    end
end