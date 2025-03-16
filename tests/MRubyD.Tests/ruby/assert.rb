##
# Verify a code block.
#
# str : A remark which will be printed in case
#       this assertion fails
# iso : The ISO reference code of the feature
#       which will be tested by this
#       assertion
def assert(str = 'assert', iso = '')
  $asserts = []

  begin
    yield
  rescue Exception => e
    $asserts.push [false, "#{e.class}: #{e.message} \n#{e.backtrace}"]
  ensure
    __report_result str, iso, $asserts
  end
end

def assert_true(obj, msg = nil, diff = nil)
  unless obj == true
    diff ||= "    Expected #{obj.inspect} to be true."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_false(obj, msg = nil, diff = nil)
  unless obj == false
    diff ||= "    Expected #{obj.inspect} to be false."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_nil(obj, msg = nil)
  unless ret = obj.nil?
    diff = "    Expected #{obj.inspect} to be nil."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_not_nil(obj, msg = nil)
  if ret = obj.nil?
    diff = "    Expected #{obj.inspect} to not be nil."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_equal(exp, act_or_msg = nil, msg = nil, &block)
  ret, exp, act, msg = _eval_assertion(:==, exp, act_or_msg, msg, block)
  if ret
    $asserts.push [true, msg]
  else
    diff = _assertion_diff(exp, act)
    $asserts.push [false, msg, diff]
  end
end

def assert_not_equal(exp, act_or_msg = nil, msg = nil, &block)
  ret, exp, act, msg = _eval_assertion(:==, exp, act_or_msg, msg, block)
  if ret
    diff = "    Expected #{act.inspect} to not be equal to #{exp.inspect}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_raise(*exc)
  msg = (exc.last.is_a? String) ? exc.pop : nil
  exc = exc.empty? ? StandardError : exc.size == 1 ? exc[0] : exc
  begin
    yield
  rescue *exc => e
    $asserts.push [true, msg]
  rescue Exception => e
    diff = "    #{exc} exception expected, not\n" \
           "    Class: <#{e.class}>\n" \
           "    Message: <#{e}>"
    $asserts.push [false, msg, diff]
  else
    diff = "    #{exc} expected but nothing was raised."
    $asserts.push [false, msg, diff]
  end
end

def _eval_assertion(meth, exp, act_or_msg, msg, block)
  if block
    exp, act, msg = exp, block.call, act_or_msg
  else
    exp, act, msg = exp, act_or_msg, msg
  end
  return exp.__send__(meth, act), exp, act, msg
end

def _assertion_diff(exp, act)
  "    Expected: #{exp.inspect}\n" \
  "      Actual: #{act.inspect}"
end

##
# Skip the test
class MRubyTestSkip < NotImplementedError; end

# def skip(cause = "")
#   raise MRubyTestSkip.new(cause)
# end