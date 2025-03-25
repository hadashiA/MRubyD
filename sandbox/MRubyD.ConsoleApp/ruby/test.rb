def fb
    n = 0
        Proc.new do
        n += 1
        case
            when n % 15 == 0
            else n
        end
    end
end

fb.call